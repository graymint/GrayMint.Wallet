using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Tests;

// A tenant's root wallet in a currency: made at the first call, the one money enters the tenant from and leaves it to,
// with no limit, and the owner "root" for it alone.
[TestClass]
public class RootWalletTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task RootWalletIsMadeOnceForEachTenantAndCurrency(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        await using var scope = testWallet.CreateScope();
        var root = await scope.Wallets.GetOrCreateRootWallet(TestWallet.TenantId, "USD", CancellationToken);
        Assert.AreEqual(WalletService.RootOwnerReferenceKey, root.OwnerReferenceKey);
        Assert.IsNull(root.MinAvailableBalance);
        Assert.AreEqual(root.WalletId,
            (await scope.Wallets.GetOrCreateRootWallet(TestWallet.TenantId, "USD", CancellationToken)).WalletId);

        var euro = await scope.Wallets.GetOrCreateRootWallet(TestWallet.TenantId, "EUR", CancellationToken);
        var otherTenant = await scope.Wallets.GetOrCreateRootWallet("2", "USD", CancellationToken);
        Assert.HasCount(3, new[] { root.WalletId, euro.WalletId, otherTenant.WalletId }.Distinct());
        Assert.AreEqual(3L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Wallets\"", CancellationToken));
    }

    // Money enters the tenant through its root, which goes below zero by what the tenant's other wallets hold.
    [TestMethod, EveryDatabase]
    public async Task RootWalletGoesBelowZeroByWhatTheOthersHold(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        await using var scope = testWallet.CreateScope();
        var root = await scope.Wallets.GetOrCreateRootWallet(TestWallet.TenantId, TestWallet.Currency, CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(root, wallet, 10));
        await testWallet.Transfer(CancellationToken, TestWallet.Request(wallet, other, 4));

        Assert.AreEqual(-10m, (await testWallet.GetWallet(root.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(6m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(4m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedBalance);
    }

    // No other wallet is taken for a root, such as one made with a limit a root doesn't have.
    [TestMethod, EveryDatabase]
    public async Task OwnerRootIsTheRootWalletsAlone(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            testWallet.CreateWallet(WalletService.RootOwnerReferenceKey, 0, CancellationToken));
        Assert.AreEqual(0L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Wallets\"", CancellationToken));
    }

    // A limit on the root would stop money entering the tenant.
    [TestMethod, EveryDatabase]
    public async Task RootWalletTakesNoLimit(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        await using var scope = testWallet.CreateScope();
        var root = await scope.Wallets.GetOrCreateRootWallet(TestWallet.TenantId, TestWallet.Currency, CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            scope.Wallets.Update(TestWallet.TenantId, root.WalletId, TestWallet.LimitUpdate(-100), CancellationToken));
        Assert.IsNull((await testWallet.GetWallet(root.WalletId, CancellationToken)).MinAvailableBalance);
        await scope.Wallets.Update(TestWallet.TenantId, root.WalletId, TestWallet.LimitUpdate(null), CancellationToken);
    }

    // Asked for twice at once, the root is made once. The first call makes it in a host's transaction that has not
    // committed yet, so the second doesn't see it, and its insert waits for the first's (on SQL Server, unless the
    // database reads committed rows by their versions, its read waits instead); then it takes the first's root.
    [TestMethod, EveryDatabase]
    public async Task RootWalletAskedForTwiceAtOnceIsOne(WalletDatabaseProvider provider)
    {
        await using var host = await TestHost.Create(provider, CancellationToken);
        await using var firstScope = host.CreateScope();
        var dbContext = firstScope.ServiceProvider.GetRequiredService<TestDbContext>();
        var firstService = firstScope.ServiceProvider.GetRequiredService<WalletService>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        var first = await firstService.GetOrCreateRootWallet(TestWallet.TenantId, TestWallet.Currency, CancellationToken);

        await using var secondScope = host.CreateScope();
        var secondService = secondScope.ServiceProvider.GetRequiredService<WalletService>();
        var second = secondService.GetOrCreateRootWallet(TestWallet.TenantId, TestWallet.Currency, CancellationToken);
        await host.Wallet.WaitForBlockedCalls(1, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        Assert.AreEqual(first.WalletId, (await second).WalletId);
        Assert.AreEqual(1L, await host.Wallet.Count("SELECT count(*) FROM gmwallet.\"Wallets\"", CancellationToken));
    }
}
