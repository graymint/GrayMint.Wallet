using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class AccountTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task AccountIsOneForItsOwnerCodeAndCurrency(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await wallet.CreateAccount("owner-1", 0, CancellationToken);
        Assert.AreEqual(TestWallet.LedgerId, account.LedgerId);
        Assert.AreEqual("owner-1", account.OwnerId);
        Assert.AreEqual(0m, account.MinBalance);
        Assert.AreEqual(0m, account.Balance);
        Assert.AreEqual(0m, account.Available);

        // the database's time, in UTC on either database
        Assert.AreEqual(DateTimeKind.Utc, account.CreatedTime.Kind);
        Assert.IsLessThan(TimeSpan.FromMinutes(1), (DateTime.UtcNow - account.CreatedTime).Duration());
        Assert.AreEqual(account.CreatedTime, (await wallet.GetAccount(account.AccountId, CancellationToken)).CreatedTime);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            wallet.CreateAccount("owner-1", null, CancellationToken));

        // another currency, another ledger, an owner that differs in case: other accounts
        await wallet.CreateAccount("owner-1", 0, CancellationToken, currency: "EUR");
        await wallet.CreateAccount("owner-1", 0, CancellationToken, ledgerId: 2);
        await wallet.CreateAccount("OWNER-1", 0, CancellationToken);

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        var found = await walletService.FindAccount(TestWallet.LedgerId, "owner-1", "credit", "USD", CancellationToken);
        Assert.AreEqual(account.AccountId, found?.AccountId);
        Assert.HasCount(2, await walletService.GetOwnerAccounts(TestWallet.LedgerId, "owner-1", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task AccountRequestIsChecked(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);

        foreach (var currency in new[] { "usd", "", "US D", "TOOLONGCURRENCY" })
            await Assert.ThrowsAsync<ArgumentException>(() => walletService.CreateAccount(TestWallet.LedgerId,
                Request("owner", "credit", currency, 0), CancellationToken), currency);

        foreach (var text in new[] { "", " owner", "owner ", new string('x', 101), "own\ner" })
            await Assert.ThrowsAsync<ArgumentException>(() => walletService.CreateAccount(TestWallet.LedgerId,
                Request(text, "credit", "USD", 0), CancellationToken), text);

        await Assert.ThrowsAsync<ArgumentException>(() => walletService.CreateAccount(TestWallet.LedgerId,
            Request("owner", " credit", "USD", 0), CancellationToken));

        // a new account has nothing to keep
        await Assert.ThrowsAsync<ArgumentException>(() => walletService.CreateAccount(TestWallet.LedgerId,
            Request("owner", "credit", "USD", 1), CancellationToken));

        // the database would round a ninth decimal away
        await Assert.ThrowsAsync<ArgumentException>(() => walletService.CreateAccount(TestWallet.LedgerId,
            Request("owner", "credit", "USD", -0.000000001m), CancellationToken));

        Assert.AreEqual(0L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Accounts\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task AccountOfAnotherLedgerIsNotThere(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken, ledgerId: 2);

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            wallet.GetAccount(account.AccountId, CancellationToken, ledgerId: 1));
        await using var dbContext = wallet.CreateDbContext();
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            TestWallet.CreateService(dbContext).SetMinBalance(1, account.AccountId, -10, CancellationToken));

        // and nothing of it was changed on the way
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken, ledgerId: 2)).MinBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task MinBalanceIsSetOnlyWhileTheAccountKeepsToIt(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 10));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        Assert.AreEqual(5m, (await walletService.SetMinBalance(TestWallet.LedgerId, account.AccountId, 5, CancellationToken)).MinBalance);
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            walletService.SetMinBalance(TestWallet.LedgerId, account.AccountId, 10.5m, CancellationToken));
        Assert.AreEqual(5m, (await wallet.GetAccount(account.AccountId, CancellationToken)).MinBalance);

        Assert.AreEqual(-100m, (await walletService.SetMinBalance(TestWallet.LedgerId, account.AccountId, -100, CancellationToken)).MinBalance);
        Assert.IsNull((await walletService.SetMinBalance(TestWallet.LedgerId, account.AccountId, null, CancellationToken)).MinBalance);
    }

    private static WalletAccountCreateRequest Request(string ownerId, string code, string currency, decimal? minBalance)
    {
        return new WalletAccountCreateRequest {
            OwnerId = ownerId,
            Code = code,
            Currency = currency,
            MinBalance = minBalance
        };
    }
}
