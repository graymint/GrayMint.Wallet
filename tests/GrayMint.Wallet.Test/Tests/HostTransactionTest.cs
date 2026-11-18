using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Tests;

// The host's rows and the money in one transaction: an order is never paid without being there, or there unpaid.
[TestClass]
public class HostTransactionTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task OrderAndItsMoneyCommitTogether(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);

        await using (var scope = host.CreateScope()) {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            dbContext.Orders.Add(new TestOrder { Name = "paid" });
            await dbContext.SaveChangesAsync(CancellationToken);
            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 10)],
                CancellationToken);
            Assert.AreEqual(10m, (await walletService.GetAccount(TestWallet.LedgerId, account.AccountId,
                CancellationToken)).Balance);

            // Nothing is seen outside before the host commits. SQL Server, unless the database reads committed
            // rows by their versions, would make the outside wait for the host's lock instead.
            if (provider == WalletDatabaseProvider.PostgreSql)
                Assert.AreEqual(0m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.AreEqual(10m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task OrderAndItsMoneyRollBackTogether(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await host.Wallet.CreateAccount("other", 0, CancellationToken);

        await using (var scope = host.CreateScope()) {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            dbContext.Orders.Add(new TestOrder { Name = "rolled back" });
            await dbContext.SaveChangesAsync(CancellationToken);
            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 10)],
                CancellationToken);

            // a refused payment of the same order leaves the host's transaction to the host
            await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() => walletService.CreateTransfers(
                TestWallet.LedgerId, [TestWallet.Request(account, other, 11)], CancellationToken));
            await transaction.RollbackAsync(CancellationToken);
        }

        Assert.AreEqual(0m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    // A chain the database refuses halfway takes back what it wrote and leaves the host's transaction usable, so
    // a host that goes on and commits commits none of the chain. Here the host's own rule refuses the second
    // transfer, as a host's wall would.
    [TestMethod, EveryDatabase]
    public async Task ChainRefusedHalfwayLeavesNothingInTheHostsTransaction(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Transfers_HostsRule\" CHECK (\"Code\" IS NULL OR \"Code\" <> 'refused')",
            CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Accounts\" ADD CONSTRAINT \"CK_Accounts_HostsRule\" CHECK (\"OwnerId\" <> 'refused')",
            CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);
        var refused = TestWallet.Request(source, account, 5);

        await using (var scope = host.CreateScope()) {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
            await Assert.ThrowsAsync<DbUpdateException>(() => walletService.CreateTransfers(TestWallet.LedgerId, [
                TestWallet.Request(source, account, 10),
                new WalletTransferRequest {
                    Key = refused.Key, DebitAccountId = refused.DebitAccountId,
                    CreditAccountId = refused.CreditAccountId, Amount = refused.Amount, Code = "refused"
                }
            ], CancellationToken));

            // a refused account goes the same way
            await Assert.ThrowsAsync<DbUpdateException>(() => walletService.CreateAccount(TestWallet.LedgerId,
                new WalletAccountCreateRequest {
                    OwnerId = "refused", Code = "credit", Currency = "USD", MinBalance = 0
                }, CancellationToken));

            dbContext.Orders.Add(new TestOrder { Name = "after the refusal" });
            await dbContext.SaveChangesAsync(CancellationToken);
            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 3)],
                CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.AreEqual(3m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
    }

    // On SQL Server, with XACT_ABORT on, an error ends the host's whole transaction: nothing of it is left to take
    // back or to look in, and the caller hears what the database refused, not what went wrong after it.
    [TestMethod]
    public async Task RefusalThatEndsTheHostsTransactionSaysWhatWasRefused()
    {
        await using var host = await TestHost.Create(WalletDatabaseProvider.SqlServer, CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Transfers_HostsRule\" CHECK (\"Code\" IS NULL OR \"Code\" <> 'refused')",
            CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);
        var refused = TestWallet.Request(source, account, 5);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET XACT_ABORT ON", CancellationToken);
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => walletService.CreateTransfers(TestWallet.LedgerId, [
            TestWallet.Request(source, account, 10),
            new WalletTransferRequest {
                Key = refused.Key, DebitAccountId = refused.DebitAccountId,
                CreditAccountId = refused.CreditAccountId, Amount = refused.Amount, Code = "refused"
            }
        ], CancellationToken));
    }

    // An account or a key that another call made meanwhile, and had not committed when this call looked for it, is
    // a conflict once that call commits, as one made before.
    [TestMethod, EveryDatabase]
    public async Task WhatAnotherCallMadeMeanwhileIsAConflict(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);
        var otherSource = await host.Wallet.CreateAccount("other-source", null, CancellationToken);
        var other = await host.Wallet.CreateAccount("other", 0, CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await walletService.CreateAccount(TestWallet.LedgerId, new WalletAccountCreateRequest {
            OwnerId = "new", Code = "credit", Currency = "USD", MinBalance = 0
        }, CancellationToken);
        await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 5, key: "k")],
            CancellationToken);

        // another call on accounts of its own, waiting for this transaction's rows
        var sameAccount = host.Wallet.CreateAccount("new", 0, CancellationToken);
        var sameKey = host.Wallet.Transfer(CancellationToken, TestWallet.Request(otherSource, other, 7, key: "k"));
        await host.Wallet.WaitForBlockedCalls(2, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => sameAccount);
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => sameKey);
        Assert.AreEqual(0m, (await host.Wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
    }

    // One scope, several of the host's transactions one after another, and calls between them in their own.
    [TestMethod, EveryDatabase]
    public async Task WalletFollowsTheHostsTransactionsOneAfterAnother(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);

        await using (var scope = host.CreateScope()) {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            await using (var committed = await dbContext.Database.BeginTransactionAsync(CancellationToken)) {
                await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 10)],
                    CancellationToken);
                await committed.CommitAsync(CancellationToken);
            }

            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 5)],
                CancellationToken);

            await using (var rolledBack = await dbContext.Database.BeginTransactionAsync(CancellationToken)) {
                await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 100)],
                    CancellationToken);
                await rolledBack.RollbackAsync(CancellationToken);
            }

            Assert.AreEqual(15m, (await walletService.GetAccount(TestWallet.LedgerId, account.AccountId,
                CancellationToken)).Balance);
        }

        Assert.AreEqual(15m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
    }

    // The wallet's context is its own, so a call never saves what the host has not saved.
    [TestMethod, EveryDatabase]
    public async Task WalletLeavesTheHostsUnsavedChangesAlone(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);

        await using (var scope = host.CreateScope()) {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            dbContext.Orders.Add(new TestOrder { Name = "not saved" });
            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 10)],
                CancellationToken);
        }

        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
        Assert.AreEqual(10m, (await host.Wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
    }

    // The host's context opens the connection, so a host's interceptor that tells the database who a connection
    // works for sees every one the wallet uses; the wallet's context never opens one by itself.
    [TestMethod, EveryDatabase]
    public async Task HostOpensTheWalletsConnection(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(CancellationToken);
        var account = await host.Wallet.CreateAccount("owner", 0, CancellationToken);
        var hostOpenedBefore = host.HostConnections.OpenedCount;

        await using (var scope = host.CreateScope()) {
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
            await walletService.CreateTransfers(TestWallet.LedgerId, [TestWallet.Request(source, account, 10)],
                CancellationToken);
            await walletService.GetAccount(TestWallet.LedgerId, account.AccountId, CancellationToken);
        }

        Assert.IsGreaterThan(hostOpenedBefore, host.HostConnections.OpenedCount);
        Assert.AreEqual(0, host.WalletConnections.OpenedCount);
    }
}
