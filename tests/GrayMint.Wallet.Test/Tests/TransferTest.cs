using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class TransferTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task PostedTransferMovesTheMoneyAtOnce(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);

        var transfers = await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 100.25m));
        Assert.HasCount(1, transfers);
        Assert.AreEqual(WalletTransferType.Posted, transfers[0].Type);
        Assert.AreEqual(100.25m, transfers[0].Amount);

        account = await wallet.GetAccount(account.AccountId, CancellationToken);
        Assert.AreEqual(100.25m, account.CreditsPosted);
        Assert.AreEqual(100.25m, account.Balance);
        Assert.AreEqual(100.25m, account.Available);
        source = await wallet.GetAccount(source.AccountId, CancellationToken);
        Assert.AreEqual(100.25m, source.DebitsPosted);
        Assert.AreEqual(-100.25m, source.Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task AccountSpendsNoMoreThanItHas(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 10));

        var exception = await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 10.01m)));
        Assert.AreEqual(account.AccountId, exception.AccountId);
        Assert.AreEqual(10m, exception.Available);

        // nothing of it is kept
        Assert.AreEqual(10m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(0m, (await wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(1L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));

        // to the last cent
        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 10));
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task CreditLimitLetsAnAccountGoBelowZeroAsFarAsIt(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await wallet.CreateAccount("owner", -50, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);

        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 50));
        Assert.AreEqual(-50m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 0.00000001m)));
    }

    [TestMethod, EveryDatabase]
    public async Task AccountWithoutLimitGoesAnyWhere(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);

        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, WalletService.MaxAmount));
        Assert.AreEqual(-WalletService.MaxAmount, (await wallet.GetAccount(source.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task TransferRequestIsChecked(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);

        foreach (var amount in new[] { 0m, -1m, 0.000000001m, WalletService.MaxAmount + 1 })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                wallet.Transfer(CancellationToken, TestWallet.Request(source, account, amount)), amount.ToString());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, source, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 1, key: "k"), TestWallet.Request(source, account, 2, key: "k")));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.Transfer(CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => wallet.Transfer(CancellationToken,
            Enumerable.Range(0, WalletService.MaxChainLength + 1)
                .Select(_ => TestWallet.Request(source, account, 1)).ToArray()));
        foreach (var key in new[] { "", " k", new string('k', WalletService.MaxKeyLength + 1) })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 1, key: key)), key);

        Assert.AreEqual(0L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task MoneyStaysInItsLedgerAndItsCurrency(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var otherLedger = await wallet.CreateAccount("owner", 0, CancellationToken, ledgerId: 2);
        var otherCurrency = await wallet.CreateAccount("owner", 0, CancellationToken, currency: "EUR");

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, otherLedger, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(source, otherCurrency, 1)));
        Assert.AreEqual(0L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task AccountTransfersComeNewestFirstByPage(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        for (var i = 1; i <= 5; i++)
            await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, i));
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, other, 100));
        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 1));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        var page = await walletService.GetAccountTransfers(TestWallet.LedgerId, account.AccountId, null, 4, CancellationToken);
        CollectionAssert.AreEqual(new[] { 1m, 5m, 4m, 3m }, page.Select(x => x.Amount).ToArray());
        var next = await walletService.GetAccountTransfers(TestWallet.LedgerId, account.AccountId, page[^1].TransferId, 4, CancellationToken);
        CollectionAssert.AreEqual(new[] { 2m, 1m }, next.Select(x => x.Amount).ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() => walletService.GetAccountTransfers(TestWallet.LedgerId,
            account.AccountId, null, WalletService.MaxPageSize + 1, CancellationToken));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            walletService.GetAccountTransfers(2, account.AccountId, null, 4, CancellationToken));
    }
}
