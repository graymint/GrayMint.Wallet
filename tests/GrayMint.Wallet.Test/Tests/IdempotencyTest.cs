using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;
using GrayMint.Wallet.Wallets;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class IdempotencyTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersSentAgainChangeNothing(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var linked = new[] {
            TestWallet.Request(source, wallet, 10, externalReferenceKey: "order-1/pay"),
            TestWallet.Request(source, wallet, 2, isPending: true, externalReferenceKey: "order-1/hold")
        };

        var first = await testWallet.Transfer(CancellationToken, linked);
        var again = await testWallet.Transfer(CancellationToken, linked);
        CollectionAssert.AreEqual(first.Select(x => x.TransferId).ToArray(), again.Select(x => x.TransferId).ToArray());

        wallet = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(10m, wallet.PostedIn);
        Assert.AreEqual(2m, wallet.PendingIn);
        Assert.AreEqual(2L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task KeyOfAnotherTransferIsRefused(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 5, externalReferenceKey: "k"));

        // each part of a transfer counts
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 6, externalReferenceKey: "k")));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, other, 5, externalReferenceKey: "k")));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(other, wallet, 5, externalReferenceKey: "k")));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 5, isPending: true, externalReferenceKey: "k")));
        var withCode = TestWallet.Request(source, wallet, 5, externalReferenceKey: "k");
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => testWallet.Transfer(CancellationToken,
            new WalletTransferCreateRequest {
                ExternalReferenceKey = withCode.ExternalReferenceKey, FromWalletId = withCode.FromWalletId,
                ToWalletId = withCode.ToWalletId, Amount = withCode.Amount, PurposeCode = "another"
            }));

        Assert.AreEqual(5m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(0m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedBalance);
    }

    // Each transfer is matched by its own key, as in TigerBeetle: sent again in another order, in part, or after its
    // pending amount was posted, it comes back as it was made and moves nothing.
    [TestMethod, EveryDatabase]
    public async Task TransfersSentAgainInAnotherOrderOrInPartChangeNothing(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var pay = TestWallet.Request(source, wallet, 10, externalReferenceKey: "pay");
        var hold = TestWallet.Request(source, wallet, 2, isPending: true, externalReferenceKey: "hold");
        var made = await testWallet.Transfer(CancellationToken, pay, hold);

        var reordered = await testWallet.Transfer(CancellationToken, hold, pay);
        CollectionAssert.AreEqual(new[] { made[1].TransferId, made[0].TransferId },
            reordered.Select(x => x.TransferId).ToArray());
        Assert.AreEqual(made[1].TransferId, (await testWallet.Transfer(CancellationToken, hold)).Single().TransferId);

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["hold"], CancellationToken);
        Assert.AreEqual(WalletTransferType.Pending, (await testWallet.Transfer(CancellationToken, hold)).Single().Type);

        wallet = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(12m, wallet.PostedIn);
        Assert.AreEqual(0m, wallet.PendingIn);
        Assert.AreEqual(3L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersMadeInPartBeforeAreRefused(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var first = TestWallet.Request(source, wallet, 5, externalReferenceKey: "k1");
        await testWallet.Transfer(CancellationToken, first);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            testWallet.Transfer(CancellationToken, first, TestWallet.Request(source, wallet, 1, externalReferenceKey: "k2")));
        Assert.AreEqual(1L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task KeyIsUniqueInItsOwnTenantOnly(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source1 = await testWallet.CreateSource(CancellationToken, tenantId: "1");
        var wallet1 = await testWallet.CreateWallet("owner", 0, CancellationToken, tenantId: "1");
        var source2 = await testWallet.CreateSource(CancellationToken, tenantId: "2");
        var wallet2 = await testWallet.CreateWallet("owner", 0, CancellationToken, tenantId: "2");

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.Create("1", [TestWallet.Request(source1, wallet1, 5, externalReferenceKey: "k")], CancellationToken);
        await scope.Transfers.Create("2", [TestWallet.Request(source2, wallet2, 7, externalReferenceKey: "k")], CancellationToken);
        Assert.AreEqual(5m, (await testWallet.GetWallet(wallet1.WalletId, CancellationToken, tenantId: "1")).PostedBalance);
        Assert.AreEqual(7m, (await testWallet.GetWallet(wallet2.WalletId, CancellationToken, tenantId: "2")).PostedBalance);

        // and a key is looked up in its tenant
        Assert.AreEqual(5m, (await scope.Transfers.GetByExternalReferenceKey("1", "k", CancellationToken)).Amount);
        Assert.AreEqual(7m, (await scope.Transfers.GetByExternalReferenceKey("2", "k", CancellationToken)).Amount);
    }

    // SQL Server compares text without case unless told otherwise, which would make these one key
    [TestMethod, EveryDatabase]
    public async Task KeysThatDifferInCaseAreTwoKeys(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);

        var lower = await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 5, externalReferenceKey: "order-1"));
        var upper = await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 7, externalReferenceKey: "ORDER-1"));
        Assert.AreNotEqual(lower[0].TransferId, upper[0].TransferId);
        Assert.AreEqual(12m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);

        await using var scope = testWallet.CreateScope();
        Assert.AreEqual(7m, (await scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, "ORDER-1", CancellationToken)).Amount);
    }

    // Text of any script, at its longest, comes back as it was sent, and two keys that differ in their last letter
    // are two keys. On SQL Server a varchar keeps such letters only under a UTF-8 collation, and its length counts
    // bytes: 字 takes 3, the most a .NET character takes.
    [TestMethod, EveryDatabase]
    public async Task TextOfAnyScriptIsKeptWhole(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        await using var scope = testWallet.CreateScope();
        var owner = new string('字', WalletLimits.MaxOwnerReferenceKeyLength);
        var code = new string('ک', WalletLimits.MaxCodeLength);
        var wallet = await scope.Wallets.Create(TestWallet.TenantId, new WalletCreateRequest {
            OwnerReferenceKey = owner,
            WalletCode = code,
            Currency = "USD",
            MinAvailableBalance = 0
        }, CancellationToken);

        var purposeCode = new string('字', WalletLimits.MaxCodeLength);
        var key = new string('字', WalletLimits.MaxExternalReferenceKeyLength - 1);
        var first = await scope.Transfers.Create(TestWallet.TenantId, [Pay(key + "一", 5)], CancellationToken);
        var second = await scope.Transfers.Create(TestWallet.TenantId, [Pay(key + "二", 7)], CancellationToken);

        Assert.AreNotEqual(first[0].TransferId, second[0].TransferId);
        var read = await testWallet.GetWallet(wallet.WalletId, CancellationToken);
        Assert.AreEqual(owner, read.OwnerReferenceKey);
        Assert.AreEqual(code, read.WalletCode);
        Assert.AreEqual(12m, read.PostedBalance);
        var transfer = await scope.Transfers.GetByExternalReferenceKey(TestWallet.TenantId, key + "二", CancellationToken);
        Assert.AreEqual(second[0].TransferId, transfer.TransferId);
        Assert.AreEqual(key + "二", transfer.ExternalReferenceKey);
        Assert.AreEqual(purposeCode, transfer.PurposeCode);
        return;

        WalletTransferCreateRequest Pay(string externalReferenceKey, decimal amount)
        {
            return new WalletTransferCreateRequest {
                ExternalReferenceKey = externalReferenceKey,
                FromWalletId = source.WalletId,
                ToWalletId = wallet.WalletId,
                Amount = amount,
                PurposeCode = purposeCode
            };
        }
    }
}
