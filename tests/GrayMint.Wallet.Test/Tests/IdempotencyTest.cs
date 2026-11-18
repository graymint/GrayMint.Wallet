using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class IdempotencyTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task ChainSentAgainChangesNothing(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var chain = new[] {
            TestWallet.Request(source, account, 10, key: "order-1/pay"),
            TestWallet.Request(source, account, 2, isPending: true, key: "order-1/hold")
        };

        var first = await wallet.Transfer(CancellationToken, chain);
        var again = await wallet.Transfer(CancellationToken, chain);
        CollectionAssert.AreEqual(first.Select(x => x.TransferId).ToArray(), again.Select(x => x.TransferId).ToArray());

        account = await wallet.GetAccount(account.AccountId, CancellationToken);
        Assert.AreEqual(10m, account.CreditsPosted);
        Assert.AreEqual(2m, account.CreditsPending);
        Assert.AreEqual(2L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task KeyOfAnotherTransferIsRefused(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 5, key: "k"));

        // each part of a transfer counts
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 6, key: "k")));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, other, 5, key: "k")));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 5, isPending: true, key: "k")));
        var withCode = TestWallet.Request(source, account, 5, key: "k");
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => wallet.Transfer(CancellationToken,
            new Transfers.WalletTransferRequest {
                Key = withCode.Key, DebitAccountId = withCode.DebitAccountId,
                CreditAccountId = withCode.CreditAccountId, Amount = withCode.Amount, Code = "another"
            }));

        Assert.AreEqual(5m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(0m, (await wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task ChainMadeInPartBeforeIsRefused(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var first = TestWallet.Request(source, account, 5, key: "k1");
        await wallet.Transfer(CancellationToken, first);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            wallet.Transfer(CancellationToken, first, TestWallet.Request(source, account, 1, key: "k2")));
        Assert.AreEqual(1L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task KeyIsUniqueInItsOwnLedgerOnly(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source1 = await wallet.CreateSource(CancellationToken, ledgerId: 1);
        var account1 = await wallet.CreateAccount("owner", 0, CancellationToken, ledgerId: 1);
        var source2 = await wallet.CreateSource(CancellationToken, ledgerId: 2);
        var account2 = await wallet.CreateAccount("owner", 0, CancellationToken, ledgerId: 2);

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        await walletService.CreateTransfers(1, [TestWallet.Request(source1, account1, 5, key: "k")], CancellationToken);
        await walletService.CreateTransfers(2, [TestWallet.Request(source2, account2, 7, key: "k")], CancellationToken);
        Assert.AreEqual(5m, (await wallet.GetAccount(account1.AccountId, CancellationToken, ledgerId: 1)).Balance);
        Assert.AreEqual(7m, (await wallet.GetAccount(account2.AccountId, CancellationToken, ledgerId: 2)).Balance);
    }

    // SQL Server compares text without case unless told otherwise, which would make these one key
    [TestMethod, EveryDatabase]
    public async Task KeysThatDifferInCaseAreTwoKeys(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);

        var lower = await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 5, key: "order-1"));
        var upper = await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 7, key: "ORDER-1"));
        Assert.AreNotEqual(lower[0].TransferId, upper[0].TransferId);
        Assert.AreEqual(12m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);

        await using var dbContext = wallet.CreateDbContext();
        Assert.AreEqual(7m, (await TestWallet.CreateService(dbContext)
            .GetTransfer(TestWallet.LedgerId, "ORDER-1", CancellationToken)).Amount);
    }
}
