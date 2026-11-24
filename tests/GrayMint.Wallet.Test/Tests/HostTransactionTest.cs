using System.Data;
using GrayMint.Wallet.Wallets;
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
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        dbContext.Orders.Add(new TestOrder { Name = "paid" });
        await dbContext.SaveChangesAsync(CancellationToken);
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 10)],
            CancellationToken);

        Assert.AreEqual(10m, (await walletService.Get(TestWallet.TenantId, wallet.WalletId,
            CancellationToken)).PostedBalance);

        // Nothing is seen outside before the host commits. SQL Server, unless the database reads committed
        // rows by their versions, would make the outside wait for the host's lock instead.
        if (provider == WalletDatabaseProvider.PostgreSql)
            Assert.AreEqual(0m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
        await transaction.CommitAsync(CancellationToken);

        Assert.AreEqual(10m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task OrderAndItsMoneyRollBackTogether(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var other = await host.Wallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        dbContext.Orders.Add(new TestOrder { Name = "rolled back" });
        await dbContext.SaveChangesAsync(CancellationToken);
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 10)],
            CancellationToken);

        // a refused payment of the same order leaves the host's transaction to the host
        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() => transferService.Create(
            TestWallet.TenantId, [TestWallet.Request(wallet, other, 11)], CancellationToken));

        await transaction.RollbackAsync(CancellationToken);

        Assert.AreEqual(0m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    // Linked transfers the database refuses halfway take back what they wrote and leave the host's transaction
    // usable, so a host that goes on and commits commits none of them. Here the host's own rule refuses the second
    // transfer, as a host's wall would.
    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersRefusedHalfwayLeaveNothingInTheHostsTransaction(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Transfers_HostsRule\" " +
            "CHECK (\"PurposeCode\" IS NULL OR \"PurposeCode\" <> 'refused')",
            CancellationToken);

        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Wallets\" ADD CONSTRAINT \"CK_Wallets_HostsRule\" " +
            "CHECK (\"OwnerReferenceKey\" <> 'refused')",
            CancellationToken);

        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var refused = TestWallet.Request(source, wallet, 5);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        var transferRefusal = await Assert.ThrowsAsync<DbUpdateException>(() => transferService.Create(
            TestWallet.TenantId, [
                TestWallet.Request(source, wallet, 10),
                new WalletTransferCreateRequest {
                    ExternalReferenceKey = refused.ExternalReferenceKey, FromWalletId = refused.FromWalletId,
                    ToWalletId = refused.ToWalletId, Amount = refused.Amount, PurposeCode = "refused"
                }
            ], CancellationToken));

        Assert.Contains("CK_Transfers_HostsRule", transferRefusal.InnerException?.Message ?? "");

        // a refused wallet goes the same way
        var walletRefusal = await Assert.ThrowsAsync<DbUpdateException>(() => walletService.Create(
            TestWallet.TenantId, new WalletCreateRequest {
                OwnerReferenceKey = "refused", WalletCode = "credit", Currency = "USD", MinAvailableBalance = 0
            }, CancellationToken));

        Assert.Contains("CK_Wallets_HostsRule", walletRefusal.InnerException?.Message ?? "");

        dbContext.Orders.Add(new TestOrder { Name = "after the refusal" });
        await dbContext.SaveChangesAsync(CancellationToken);
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 3)],
            CancellationToken);

        await transaction.CommitAsync(CancellationToken);

        Assert.AreEqual(3m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
    }

    // A refusal after the transfers are written and the first wallet's totals have moved takes all of it back too:
    // in the host's transaction, which stays usable, and in a call's own. Here the host's own rule caps a wallet.
    [TestMethod, EveryDatabase]
    public async Task RefusalWhileTheTotalsMoveTakesBackTheTransfersToo(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Wallets\" ADD CONSTRAINT \"CK_Wallets_HostsCap\" " +
            "CHECK (\"OwnerReferenceKey\" <> 'capped' OR \"PostedIn\" <= 5)", CancellationToken);

        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var capped = await host.Wallet.CreateWallet("capped", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        var inHostsTransaction = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
            transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, capped, 10)],
                CancellationToken));

        Assert.Contains("CK_Wallets_HostsCap", inHostsTransaction.Message);

        dbContext.Orders.Add(new TestOrder { Name = "after the refusal" });
        await dbContext.SaveChangesAsync(CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        var inItsOwn = await Assert.ThrowsAsync<System.Data.Common.DbException>(() =>
            host.Wallet.Transfer(CancellationToken, TestWallet.Request(source, capped, 10)));

        Assert.Contains("CK_Wallets_HostsCap", inItsOwn.Message);

        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(0m, (await host.Wallet.GetWallet(source.WalletId, cancellationToken: CancellationToken)).PostedOut);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
    }

    // On SQL Server, with XACT_ABORT on, an error ends the host's whole transaction: nothing of it is left to take
    // back or to look in, and the caller hears what the database refused, not what went wrong after it.
    [TestMethod]
    public async Task RefusalThatEndsTheHostsTransactionSaysWhatWasRefused()
    {
        await using var host = await TestHost.Create(WalletDatabaseProvider.SqlServer, CancellationToken);
        await host.Wallet.Execute(
            "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Transfers_HostsRule\" " +
            "CHECK (\"PurposeCode\" IS NULL OR \"PurposeCode\" <> 'refused')",
            CancellationToken);

        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var refused = TestWallet.Request(source, wallet, 5);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET XACT_ABORT ON", CancellationToken);
        var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => transferService.Create(
            TestWallet.TenantId, [
                TestWallet.Request(source, wallet, 10),
                new WalletTransferCreateRequest {
                    ExternalReferenceKey = refused.ExternalReferenceKey, FromWalletId = refused.FromWalletId,
                    ToWalletId = refused.ToWalletId, Amount = refused.Amount, PurposeCode = "refused"
                }
            ], CancellationToken));

        Assert.Contains("CK_Transfers_HostsRule", exception.InnerException?.Message ?? "");
    }

    // A conflict is told as one only while the host's transaction can go on. A refused insert that ends it (XACT_ABORT
    // on SQL Server) goes on as the database's error, though another call took the key meanwhile: a conflict would
    // let the host go on in a transaction that is gone, whose next commands run each on its own. The host here reads
    // by row versions (snapshot), so the call misses the key the other call holds uncommitted, and its insert finds it.
    [TestMethod]
    public async Task ConflictThatEndsTheHostsTransactionGoesOnAsTheDatabasesError()
    {
        await using var host = await TestHost.Create(WalletDatabaseProvider.SqlServer, CancellationToken);
        await host.Wallet.Execute("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON", CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var otherSource = await host.Wallet.CreateWallet("other-source", null, cancellationToken: CancellationToken);
        var other = await host.Wallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        await using var firstScope = host.CreateScope();
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<TestDbContext>();
        await using var first = await firstDbContext.Database.BeginTransactionAsync(CancellationToken);
        await firstScope.ServiceProvider.GetRequiredService<WalletTransferService>().Create(TestWallet.TenantId,
            [TestWallet.Request(source, wallet, 5, externalReferenceKey: "k")], CancellationToken);

        // the other call, on wallets of its own, waits at its insert for the first one's key
        await using var secondScope = host.CreateScope();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<TestDbContext>();
        await using var second = await secondDbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot,
            CancellationToken);

        await secondDbContext.Database.ExecuteSqlRawAsync("SET XACT_ABORT ON", CancellationToken);
        var sameKey = secondScope.ServiceProvider.GetRequiredService<WalletTransferService>().Create(
            TestWallet.TenantId, [TestWallet.Request(otherSource, other, 7, externalReferenceKey: "k")], CancellationToken);

        await host.Wallet.WaitForBlockedCalls(1, CancellationToken);
        await first.CommitAsync(CancellationToken);

        var exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => sameKey);
        Assert.Contains("IX_Transfers_TenantId_ExternalReferenceKey", exception.InnerException?.Message ?? "");
        Assert.AreEqual(0m, (await host.Wallet.GetWallet(other.WalletId, cancellationToken: CancellationToken)).PostedBalance);
    }

    // A wallet or a key that another call made meanwhile, and had not committed when this call looked for it, is
    // a conflict once that call commits, as one made before.
    [TestMethod, EveryDatabase]
    public async Task WhatAnotherCallMadeMeanwhileIsAConflict(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var otherSource = await host.Wallet.CreateWallet("other-source", null, cancellationToken: CancellationToken);
        var other = await host.Wallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await walletService.Create(TestWallet.TenantId, new WalletCreateRequest {
            OwnerReferenceKey = "new", WalletCode = "credit", Currency = "USD", MinAvailableBalance = 0
        }, CancellationToken);

        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 5, externalReferenceKey: "k")],
            CancellationToken);

        // another call on wallets of its own, waiting for this transaction's rows
        var sameWallet = host.Wallet.CreateWallet("new", 0, cancellationToken: CancellationToken);
        var sameKey = host.Wallet.Transfer(CancellationToken, TestWallet.Request(otherSource, other, 7, externalReferenceKey: "k"));
        await host.Wallet.WaitForBlockedCalls(2, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => sameWallet);
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => sameKey);
        Assert.AreEqual(0m, (await host.Wallet.GetWallet(other.WalletId, cancellationToken: CancellationToken)).PostedBalance);
    }

    // One scope, several of the host's transactions one after another, and calls between them in their own.
    [TestMethod, EveryDatabase]
    public async Task WalletFollowsTheHostsTransactionsOneAfterAnother(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var committed = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 10)],
            CancellationToken);

        await committed.CommitAsync(CancellationToken);

        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 5)],
            CancellationToken);

        await using var rolledBack = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 100)],
            CancellationToken);

        await rolledBack.RollbackAsync(CancellationToken);

        Assert.AreEqual(15m, (await walletService.Get(TestWallet.TenantId, wallet.WalletId,
            CancellationToken)).PostedBalance);

        Assert.AreEqual(15m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
    }

    // The wallet's context is its own, so a call never saves what the host has not saved.
    [TestMethod, EveryDatabase]
    public async Task WalletLeavesTheHostsUnsavedChangesAlone(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);

        await using var scope = host.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        dbContext.Orders.Add(new TestOrder { Name = "not saved" });
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 10)],
            CancellationToken);

        Assert.AreEqual(0L, await host.Wallet.Count("SELECT count(*) FROM \"Orders\"", CancellationToken));
        Assert.AreEqual(10m, (await host.Wallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
    }

    // The host's context opens the connection, so a host's interceptor that tells the database who a connection
    // works for sees every one the wallet uses; the wallet's context never opens one by itself.
    [TestMethod, EveryDatabase]
    public async Task HostOpensTheWalletsConnection(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        var source = await host.Wallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await host.Wallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var hostOpenedBefore = host.HostConnections.OpenedCount;

        await using var scope = host.CreateScope();
        var transferService = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
        await transferService.Create(TestWallet.TenantId, [TestWallet.Request(source, wallet, 10)],
            CancellationToken);

        await walletService.Get(TestWallet.TenantId, wallet.WalletId, CancellationToken);

        Assert.IsGreaterThan(hostOpenedBefore, host.HostConnections.OpenedCount);
        Assert.AreEqual(0, host.WalletConnections.OpenedCount);
    }
}
