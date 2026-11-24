using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

// A wallet's transactions are its statement: each transfer's amount, below 0 when it went out, and the balances it
// left.
[TestClass]
public class WalletTransactionTest : TestBase
{
    // Every type of transfer leaves its balances on both its wallets, and the last transaction has the ones the wallet
    // has now.
    [TestMethod, EveryDatabase]
    public async Task TransactionsKeepTheBalancesEachTransferLeft(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var customer = await testWallet.CreateWallet("customer", 0);
        var shop = await testWallet.CreateWallet("shop", 0);
        await testWallet.Transfer(TestWallet.Request(source, customer, 100));
        await testWallet.Transfer(TestWallet.Request(customer, shop, 12.5m));
        await testWallet.Transfer(TestWallet.Request(customer, shop, 20, isPending: true, externalReferenceKey: "held"));

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], TestCt);
        await testWallet.Transfer(TestWallet.Request(customer, shop, 5, isPending: true, externalReferenceKey: "dropped"));

        await scope.Transfers.VoidPending(TestWallet.TenantId, ["dropped"], TestCt);
        await testWallet.Transfer(TestWallet.Request(shop, customer, 12.5m));

        // money pending out is held at once, and comes back by its void, whose amount says so
        var transactions = await testWallet.GetTransactions(customer);
        Assert.AreSequenceEqual([
            WalletTransferType.Posted, WalletTransferType.Posted, WalletTransferType.Pending,
            WalletTransferType.PostPending, WalletTransferType.Pending, WalletTransferType.VoidPending,
            WalletTransferType.Posted
        ], transactions.Select(x => x.Transfer.Type).ToArray());

        Assert.AreSequenceEqual([100m, -12.5m, -20m, -20m, -5m, 5m, 12.5m],
            transactions.Select(x => x.Amount).ToArray());

        Assert.AreSequenceEqual([100m, 87.5m, 87.5m, 67.5m, 67.5m, 67.5m, 80m],
            transactions.Select(x => x.PostedBalance).ToArray());

        Assert.AreSequenceEqual([100m, 87.5m, 67.5m, 67.5m, 62.5m, 67.5m, 80m],
            transactions.Select(x => x.AvailableBalance).ToArray());

        var customerNow = await testWallet.GetWallet(customer.WalletId);
        Assert.AreEqual(customerNow.PostedBalance, transactions[^1].PostedBalance);
        Assert.AreEqual(customerNow.AvailableBalance, transactions[^1].AvailableBalance);

        // money pending in counts only once it is posted
        var shopTransactions = await testWallet.GetTransactions(shop);
        Assert.AreSequenceEqual([12.5m, 20m, 20m, 5m, -5m, -12.5m],
            shopTransactions.Select(x => x.Amount).ToArray());

        Assert.AreSequenceEqual([12.5m, 12.5m, 32.5m, 32.5m, 32.5m, 20m],
            shopTransactions.Select(x => x.PostedBalance).ToArray());

        Assert.AreSequenceEqual([12.5m, 12.5m, 32.5m, 32.5m, 32.5m, 20m],
            shopTransactions.Select(x => x.AvailableBalance).ToArray());

        Assert.IsTrue(shopTransactions.All(x => x.WalletId == shop.WalletId));
    }

    // Linked transfers through one wallet each leave their own balances, in their order.
    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersEachLeaveTheirOwnBalances(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10),
            TestWallet.Request(wallet, other, 4),
            TestWallet.Request(wallet, other, 3));

        var transactions = await testWallet.GetTransactions(wallet);
        Assert.AreSequenceEqual([10m, -4m, -3m], transactions.Select(x => x.Amount).ToArray());
        Assert.AreSequenceEqual([10m, 6m, 3m], transactions.Select(x => x.PostedBalance).ToArray());
        var otherTransactions = await testWallet.GetTransactions(other);
        Assert.AreSequenceEqual([4m, 7m], otherTransactions.Select(x => x.PostedBalance).ToArray());
    }

    // A transfer or a settlement sent again changes nothing, so it makes no wallet transaction either.
    [TestMethod, EveryDatabase]
    public async Task TransferSentAgainMakesNoSecondTransaction(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var request = TestWallet.Request(source, wallet, 10, isPending: true, externalReferenceKey: "held");
        await testWallet.Transfer(request);
        await testWallet.Transfer(request);
        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], TestCt);
        await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], TestCt);

        // the pending transfer and its post, each on both wallets
        Assert.AreEqual(4L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"WalletTransactions\""));
    }

    [TestMethod, EveryDatabase]
    public async Task WalletTransactionsComeNewestFirstByPage(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);
        for (var i = 1; i <= 5; i++)
            await testWallet.Transfer(TestWallet.Request(source, wallet, i));
        await testWallet.Transfer(TestWallet.Request(source, other, 100));
        await testWallet.Transfer(TestWallet.Request(wallet, other, 1));

        await using var scope = testWallet.CreateScope();
        var page = await scope.Wallets.GetTransactions(TestWallet.TenantId, wallet.WalletId, null, 4, TestCt);
        Assert.AreSequenceEqual([-1m, 5m, 4m, 3m], page.Select(x => x.Amount).ToArray());
        Assert.AreSequenceEqual([14m, 15m, 10m, 6m], page.Select(x => x.PostedBalance).ToArray());
        var next = await scope.Wallets.GetTransactions(TestWallet.TenantId, wallet.WalletId,
            page[^1].Transfer.TransferId, 4, TestCt);

        Assert.AreSequenceEqual([2m, 1m], next.Select(x => x.Amount).ToArray());

        foreach (var count in new[] { 0, WalletLimits.MaxPageSize + 1 })
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => scope.Wallets.GetTransactions(
                TestWallet.TenantId, wallet.WalletId, null, count, TestCt));

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Wallets.GetTransactions("2", wallet.WalletId, null, 4, TestCt));
    }
}
