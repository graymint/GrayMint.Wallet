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
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);

        var transfers = await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 100.25m));
        Assert.HasCount(1, transfers);
        Assert.AreEqual(WalletTransferType.Posted, transfers[0].Type);
        Assert.AreEqual(100.25m, transfers[0].Amount);

        wallet = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(100.25m, wallet.PostedIn);
        Assert.AreEqual(100.25m, wallet.PostedBalance);
        Assert.AreEqual(100.25m, wallet.AvailableBalance);
        source = await testWallet.GetWallet(source.WalletId, CancellationToken);
        Assert.AreEqual(100.25m, source.PostedOut);
        Assert.AreEqual(-100.25m, source.PostedBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task WalletSpendsNoMoreThanItHas(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 10));

        var exception = await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 10.01m)));
        Assert.AreEqual(wallet.WalletId, exception.WalletId);
        Assert.AreEqual(10m, exception.AvailableBalance);

        // nothing of it is kept
        Assert.AreEqual(10m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(0m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(1L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));

        // to the last cent
        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 10));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task CreditLimitLetsAWalletGoBelowZeroAsFarAsIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", -50, CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);

        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 50));
        Assert.AreEqual(-50m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 0.00000001m)));
    }

    [TestMethod, EveryDatabase]
    public async Task WalletWithoutLimitGoesAnyWhere(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);

        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, WalletLimits.MaxAmount));
        Assert.AreEqual(-WalletLimits.MaxAmount, (await testWallet.GetWallet(source.WalletId, CancellationToken)).PostedBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task TransferRequestIsChecked(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);

        foreach (var amount in new[] { 0m, -1m, 0.000000001m, WalletLimits.MaxAmount + 1 })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, amount)), amount.ToString());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, source, 1)));
        // one wallet, its id written two ways
        await Assert.ThrowsAsync<ArgumentException>(() => testWallet.Transfer(CancellationToken,
            new WalletTransferCreateRequest {
                ExternalReferenceKey = "k", FromWalletId = source.WalletId, ToWalletId = "0" + source.WalletId,
                Amount = 1
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, wallet, 1, externalReferenceKey: "k"), TestWallet.Request(source, wallet, 2, externalReferenceKey: "k")));
        await Assert.ThrowsAsync<ArgumentException>(() => testWallet.Transfer(CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => testWallet.Transfer(CancellationToken,
            Enumerable.Range(0, WalletLimits.MaxLinkedTransfers + 1)
                .Select(_ => TestWallet.Request(source, wallet, 1)).ToArray()));
        foreach (var key in new[] { "", " k", new string('k', WalletLimits.MaxExternalReferenceKeyLength + 1) })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 1, externalReferenceKey: key)), key);
        foreach (var code in new[] { "", " c", new string('c', WalletLimits.MaxCodeLength + 1) })
            await Assert.ThrowsAsync<ArgumentException>(() => testWallet.Transfer(CancellationToken,
                new WalletTransferCreateRequest {
                    ExternalReferenceKey = "k", FromWalletId = source.WalletId, ToWalletId = wallet.WalletId, Amount = 1,
                    PurposeCode = code
                }), code);

        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    // A lookup takes only what a write would take: SQL Server would find "k" for "k ", and a missing key would find
    // a post or a void, which have none.
    [TestMethod, EveryDatabase]
    public async Task TransferLookupIsChecked(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 1, isPending: true, externalReferenceKey: "k"));
        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["k"], CancellationToken);

        foreach (var key in new[] { "k ", " k", "", null })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, key!, CancellationToken), key ?? "null");
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, "other", CancellationToken));
        Assert.AreEqual(1m, (await scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, "k", CancellationToken)).Amount);
    }

    [TestMethod, EveryDatabase]
    public async Task MoneyStaysInItsTenantAndItsCurrency(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var otherTenant = await testWallet.CreateWallet("owner", 0, CancellationToken, tenantId: "2");
        var otherCurrency = await testWallet.CreateWallet("owner", 0, CancellationToken, currency: "EUR");

        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, otherTenant, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, otherCurrency, 1)));
        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
    }
}
