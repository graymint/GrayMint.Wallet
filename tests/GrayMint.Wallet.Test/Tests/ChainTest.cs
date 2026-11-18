using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class ChainTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task ChainIsMadeWholeOrNotAtAll(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);

        await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() => wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 10),
            TestWallet.Request(account, other, 20)));

        Assert.AreEqual(0L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken)).CreditsPosted);
        Assert.AreEqual(0m, (await wallet.GetAccount(source.AccountId, CancellationToken)).DebitsPosted);
    }

    [TestMethod, EveryDatabase]
    public async Task EachTransferOfAChainSeesTheOnesBeforeIt(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);

        var transfers = await wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 10),
            TestWallet.Request(account, other, 10));
        Assert.HasCount(2, transfers);
        Assert.IsLessThan(transfers[1].TransferId, transfers[0].TransferId);
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(10m, (await wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
    }

    // A pending credit is not there yet, not even for the next transfer of its own chain.
    [TestMethod, EveryDatabase]
    public async Task PendingCreditIsNotSpentInsideItsChain(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);

        await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() => wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 10, isPending: true),
            TestWallet.Request(account, other, 10)));
        Assert.AreEqual(0L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }
}
