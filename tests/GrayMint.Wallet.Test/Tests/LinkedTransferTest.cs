using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class LinkedTransferTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersAreMadeAllOrNone(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() => testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, wallet, 10),
            TestWallet.Request(wallet, other, 20)));

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedIn);
        Assert.AreEqual(0m, (await testWallet.GetWallet(source.WalletId, cancellationToken: CancellationToken)).PostedOut);
    }

    [TestMethod, EveryDatabase]
    public async Task EachLinkedTransferSeesTheOnesBeforeIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        var transfers = await testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, wallet, 10),
            TestWallet.Request(wallet, other, 10));

        Assert.HasCount(2, transfers);
        Assert.IsLessThan(long.Parse(transfers[1].TransferId), long.Parse(transfers[0].TransferId));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, cancellationToken: CancellationToken)).PostedBalance);
        Assert.AreEqual(10m, (await testWallet.GetWallet(other.WalletId, cancellationToken: CancellationToken)).PostedBalance);
    }

    // Money pending in is not there yet, not even for the next of the transfers linked to it.
    [TestMethod, EveryDatabase]
    public async Task MoneyPendingInIsNotSpentByTheTransfersLinkedToIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(cancellationToken: CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, cancellationToken: CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, cancellationToken: CancellationToken);

        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() => testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, wallet, 10, isPending: true),
            TestWallet.Request(wallet, other, 10)));

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }
}
