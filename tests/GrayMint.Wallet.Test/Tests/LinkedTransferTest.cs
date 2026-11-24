using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class LinkedTransferTest : TestBase
{
    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersAreMadeAllOrNone(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);

        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() => testWallet.Transfer(TestWallet.Request(source, wallet, 10),
            TestWallet.Request(wallet, other, 20)));

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\""));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId)).PostedIn);
        Assert.AreEqual(0m, (await testWallet.GetWallet(source.WalletId)).PostedOut);
    }

    [TestMethod, EveryDatabase]
    public async Task EachLinkedTransferSeesTheOnesBeforeIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);

        var transfers = await testWallet.Transfer(TestWallet.Request(source, wallet, 10),
            TestWallet.Request(wallet, other, 10));

        Assert.HasCount(2, transfers);
        Assert.IsLessThan(long.Parse(transfers[1].TransferId), long.Parse(transfers[0].TransferId));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId)).PostedBalance);
        Assert.AreEqual(10m, (await testWallet.GetWallet(other.WalletId)).PostedBalance);
    }

    // Money pending in is not there yet, not even for the next of the transfers linked to it.
    [TestMethod, EveryDatabase]
    public async Task MoneyPendingInIsNotSpentByTheTransfersLinkedToIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);

        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() => testWallet.Transfer(TestWallet.Request(source, wallet, 10, isPending: true),
            TestWallet.Request(wallet, other, 10)));

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\""));
    }
}
