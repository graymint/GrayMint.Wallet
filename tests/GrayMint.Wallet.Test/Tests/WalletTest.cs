using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Wallets;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class WalletTest : TestBase
{
    [TestMethod, EveryDatabase]
    public async Task WalletIsOneForItsOwnerCodeAndCurrency(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var wallet = await testWallet.CreateWallet("owner-1", 0);
        Assert.AreEqual(TestWallet.TenantId, wallet.TenantId);
        Assert.AreEqual("owner-1", wallet.OwnerReferenceKey);
        Assert.AreEqual(0m, wallet.MinAvailableBalance);
        Assert.AreEqual(0m, wallet.PostedBalance);
        Assert.AreEqual(0m, wallet.AvailableBalance);

        // the database's time, in UTC on either database
        Assert.AreEqual(DateTimeKind.Utc, wallet.CreatedTime.Kind);
        Assert.IsLessThan(TimeSpan.FromMinutes(1), (DateTime.UtcNow - wallet.CreatedTime).Duration());
        Assert.AreEqual(wallet.CreatedTime, (await testWallet.GetWallet(wallet.WalletId)).CreatedTime);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.CreateWallet("owner-1", null));

        // another currency, another tenant, an owner that differs in case: other wallets
        await testWallet.CreateWallet("owner-1", 0, currency: "EUR");
        await testWallet.CreateWallet("owner-1", 0, tenantId: "2");
        await testWallet.CreateWallet("OWNER-1", 0);

        await using var scope = testWallet.CreateScope();
        var found = await scope.Wallets.Find(TestWallet.TenantId, "owner-1", "credit", "USD", TestCt);
        Assert.AreEqual(wallet.WalletId, found?.WalletId);
        Assert.IsNull(await scope.Wallets.Find(TestWallet.TenantId, "owner-1", "cashback", "USD", TestCt));
        Assert.HasCount(2, await scope.Wallets.GetByOwner(TestWallet.TenantId, "owner-1", TestCt));
    }

    [TestMethod, EveryDatabase]
    public async Task WalletRequestIsChecked(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        await using var scope = testWallet.CreateScope();

        foreach (var currency in new[] { "usd", "", "US D", "TOOLONGCURRENCY" })
            await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
                Request("owner", "credit", currency, 0), TestCt), currency);

        foreach (var text in new[] { "", " owner", "owner ", new string('x', 101), "own\ner" })
            await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
                Request(text, "credit", "USD", 0), TestCt), text);

        await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
            Request("owner", " credit", "USD", 0), TestCt));

        await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
            Request("owner", new string('c', WalletLimits.MaxCodeLength + 1), "USD", 0), TestCt));

        await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
            Request("owner", "credit", "USD", -WalletLimits.MaxAmount - 1), TestCt));

        // a new wallet has nothing to keep
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
            Request("owner", "credit", "USD", 1), TestCt));

        // the database would round a ninth decimal away
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Create(TestWallet.TenantId,
            Request("owner", "credit", "USD", -0.000000001m), TestCt));

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Wallets\""));
    }

    // a lookup takes only what a write would take: SQL Server would find "owner" for "owner "
    [TestMethod, EveryDatabase]
    public async Task WalletLookupIsChecked(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        await testWallet.CreateWallet("owner", 0);
        await using var scope = testWallet.CreateScope();

        foreach (var (ownerReferenceKey, code, currency) in new[] {
                     ("owner ", "credit", "USD"), ("owner", "credit ", "USD"), ("owner", "credit", "usd")
                 })
            await Assert.ThrowsAsync<ArgumentException>(() => scope.Wallets.Find(TestWallet.TenantId,
                ownerReferenceKey, code, currency, TestCt), $"{ownerReferenceKey}|{code}|{currency}");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Wallets.GetByOwner(TestWallet.TenantId, "owner ", TestCt));

        Assert.IsNotNull(await scope.Wallets.Find(TestWallet.TenantId, "owner", "credit", "USD", TestCt));
    }

    [TestMethod, EveryDatabase]
    public async Task WalletOfAnotherTenantIsNotThere(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var wallet = await testWallet.CreateWallet("owner", 0, tenantId: "2");

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            testWallet.GetWallet(wallet.WalletId, tenantId: "1"));

        await using var scope = testWallet.CreateScope();
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Wallets.Update("1", wallet.WalletId, TestWallet.LimitUpdate(-10), TestCt));

        // and nothing of it was changed on the way
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, tenantId: "2")).MinAvailableBalance);
    }

    // Ids are text, and one that is no number, or a number beyond the key's type, names nothing: every call takes it
    // as one that is not there.
    [TestMethod, EveryDatabase]
    public async Task IdThatIsNoNumberIsNotThere(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await using var scope = testWallet.CreateScope();

        foreach (var walletId in new[] { "abc", "-1", " 1", "1.0", "2147483648" })
            await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
                scope.Wallets.Get(TestWallet.TenantId, walletId, TestCt), walletId);

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Wallets.Get("one", wallet.WalletId, TestCt));

        await Assert.ThrowsExactlyAsync<NotExistsException>(() => scope.Wallets.Create("one",
            Request("owner", "credit", "USD", 0), TestCt));

        await Assert.ThrowsExactlyAsync<NotExistsException>(() => scope.Transfers.Create(TestWallet.TenantId, [
            new WalletTransferCreateRequest {
                ExternalReferenceKey = "k", FromWalletId = source.WalletId, ToWalletId = "x", Amount = 1
            }
        ], TestCt));

        await Assert.ThrowsExactlyAsync<NotExistsException>(() => scope.Wallets.GetTransactions(TestWallet.TenantId,
            wallet.WalletId, beforeTransferId: "x", 10, TestCt));

        // an id given as the wallet gives it names the wallet
        Assert.AreEqual(wallet.WalletId,
            (await scope.Wallets.Get(TestWallet.TenantId, wallet.WalletId, TestCt)).WalletId);

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\""));
    }

    [TestMethod, EveryDatabase]
    public async Task MinAvailableBalanceIsSetOnlyWhileTheWalletKeepsToIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10));

        await using var scope = testWallet.CreateScope();
        Assert.AreEqual(5m, (await scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(5), TestCt)).MinAvailableBalance);
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(10.5m), TestCt));

        Assert.AreEqual(5m, (await testWallet.GetWallet(wallet.WalletId)).MinAvailableBalance);

        Assert.AreEqual(-100m, (await scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(-100), TestCt)).MinAvailableBalance);
        Assert.AreEqual(-100m, (await scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, new WalletUpdateRequest(), TestCt)).MinAvailableBalance);
        Assert.IsNull((await scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(null), TestCt)).MinAvailableBalance);

        foreach (var minAvailableBalance in new[] { -0.000000001m, -WalletLimits.MaxAmount - 1 })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(minAvailableBalance), TestCt));
    }

    private static WalletCreateRequest Request(string ownerReferenceKey, string code, string currency, decimal? minAvailableBalance)
    {
        return new WalletCreateRequest {
            OwnerReferenceKey = ownerReferenceKey,
            WalletCode = code,
            Currency = currency,
            MinAvailableBalance = minAvailableBalance
        };
    }
}
