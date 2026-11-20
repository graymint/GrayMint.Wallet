using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class PendingTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task PendingOutHoldsTheMoneyAndPendingInIsNotThereYet(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await CreateFunded(testWallet, 10);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);

        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 6, isPending: true, externalReferenceKey: "hold"));
        wallet = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(10m, wallet.PostedBalance);
        Assert.AreEqual(4m, wallet.AvailableBalance);
        other = await testWallet.GetWallet(other.WalletId, CancellationToken);
        Assert.AreEqual(6m, other.PendingIn);
        Assert.AreEqual(0m, other.AvailableBalance);

        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 5)));
        await Assert.ThrowsExactlyAsync<InsufficientFundsException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(other, wallet, 1)));
    }

    [TestMethod, EveryDatabase]
    public async Task PostMakesThePendingAmountPostedAndVoidGivesItBack(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await CreateFunded(testWallet, 10);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken,
            TestWallet.Request(wallet, other, 6, isPending: true, externalReferenceKey: "post-me"),
            TestWallet.Request(wallet, other, 3, isPending: true, externalReferenceKey: "void-me"));

        await using var scope = testWallet.CreateScope();
        var posted = await scope.Transfers.PostPending(TestWallet.TenantId, ["post-me"], CancellationToken);
        Assert.AreEqual(WalletTransferType.PostPending, posted[0].Type);
        Assert.IsNull(posted[0].ExternalReferenceKey);
        var voided = await scope.Transfers.VoidPending(TestWallet.TenantId, ["void-me"], CancellationToken);
        Assert.AreEqual(WalletTransferType.VoidPending, voided[0].Type);

        var pending = await scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, "post-me", CancellationToken);
        Assert.AreEqual(posted[0].TransferId, (await scope.Transfers.FindSettlement(TestWallet.TenantId, pending.TransferId, CancellationToken))?.TransferId);

        wallet = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(0m, wallet.PendingOut);
        Assert.AreEqual(6m, wallet.PostedOut);
        Assert.AreEqual(4m, wallet.AvailableBalance);
        other = await testWallet.GetWallet(other.WalletId, CancellationToken);
        Assert.AreEqual(0m, other.PendingIn);
        Assert.AreEqual(6m, other.AvailableBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task SettlementSentAgainChangesNothingAndTheOtherWayIsRefused(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await CreateFunded(testWallet, 10);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken,
            TestWallet.Request(wallet, other, 6, isPending: true, externalReferenceKey: "p1"),
            TestWallet.Request(wallet, other, 2, isPending: true, externalReferenceKey: "p2"));

        await using var scope = testWallet.CreateScope();
        var first = await scope.Transfers.PostPending(TestWallet.TenantId, ["p1"], CancellationToken);
        var again = await scope.Transfers.PostPending(TestWallet.TenantId, ["p1"], CancellationToken);
        Assert.AreEqual(first[0].TransferId, again[0].TransferId);

        // in a list, the one settled before comes back, the other is settled now, each in its place
        var both = await scope.Transfers.PostPending(TestWallet.TenantId, ["p2", "p1"], CancellationToken);
        Assert.AreEqual(first[0].TransferId, both[1].TransferId);
        Assert.AreEqual(2m, both[0].Amount);
        Assert.AreEqual(8m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedOut);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            scope.Transfers.VoidPending(TestWallet.TenantId, ["p1"], CancellationToken));
        Assert.AreEqual(8m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedBalance);
    }

    [TestMethod, EveryDatabase]
    public async Task OnlyAPendingTransferOfTheTenantIsSettled(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await CreateFunded(testWallet, 10);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 1, externalReferenceKey: "posted"));
        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 1, isPending: true, externalReferenceKey: "voided"));

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.VoidPending(TestWallet.TenantId, ["voided"], CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, ["posted"], CancellationToken));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, ["voided"], CancellationToken));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, ["nothing"], CancellationToken));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            scope.Transfers.PostPending("2", ["voided"], CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, ["voided", "voided"], CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, [], CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Transfers.PostPending(TestWallet.TenantId, ["voided "], CancellationToken));
    }

    // a key is unique in its tenant only, so another tenant's pending transfer of the same key is left as it is
    [TestMethod, EveryDatabase]
    public async Task PendingTransferOfTheSameKeyInAnotherTenantIsLeftAlone(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await CreateFunded(testWallet, 10);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        var source2 = await testWallet.CreateSource(CancellationToken, tenantId: "2");
        var wallet2 = await testWallet.CreateWallet("owner", 0, CancellationToken, tenantId: "2");

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.Create(TestWallet.TenantId,
            [TestWallet.Request(wallet, other, 3, isPending: true, externalReferenceKey: "held")], CancellationToken);
        var heldInTenant2 = await scope.Transfers.Create("2",
            [TestWallet.Request(source2, wallet2, 4, isPending: true, externalReferenceKey: "held")], CancellationToken);

        var settlements = await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], CancellationToken);
        Assert.AreEqual(TestWallet.TenantId, settlements.Single().TenantId);
        Assert.AreEqual(3m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedIn);
        Assert.IsNull(await scope.Transfers.FindSettlement("2", heldInTenant2[0].TransferId, CancellationToken));
        Assert.AreEqual(4m, (await testWallet.GetWallet(wallet2.WalletId, CancellationToken, tenantId: "2")).PendingIn);

        // a settlement is looked up in its tenant
        var settled = settlements.Single().PendingTransferId ?? throw new InvalidOperationException("No pending id.");
        Assert.IsNotNull(await scope.Transfers.FindSettlement(TestWallet.TenantId, settled, CancellationToken));
        Assert.IsNull(await scope.Transfers.FindSettlement("2", settled, CancellationToken));
    }

    private async Task<Wallets.WalletInfo> CreateFunded(TestWallet testWallet, decimal amount)
    {
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, amount));
        return wallet;
    }
}
