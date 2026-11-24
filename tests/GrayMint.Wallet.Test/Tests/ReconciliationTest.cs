using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class ReconciliationTest : TestBase
{
    [TestMethod, EveryDatabase]
    public async Task ReconciledBooksReportNothing(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", -20);
        await testWallet.CreateWallet("unused", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 50),
            TestWallet.Request(wallet, other, 7, isPending: true, externalReferenceKey: "posted"),
            TestWallet.Request(wallet, other, 3, isPending: true, externalReferenceKey: "voided"),
            TestWallet.Request(wallet, other, 2, isPending: true, externalReferenceKey: "held"),
            TestWallet.Request(other, wallet, 15));

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["posted"], TestCt);
        await scope.Transfers.VoidPending(TestWallet.TenantId, ["voided"], TestCt);

        var report = await scope.Reconciliation.Reconcile(TestCt);
        Assert.IsEmpty(report.WalletDiscrepancies);
        Assert.IsEmpty(report.CurrencyImbalances);
        Assert.IsTrue(report.IsReconciled);
        Assert.AreEqual(4, report.WalletCount);
        wallet = await testWallet.GetWallet(wallet.WalletId);
        Assert.AreEqual(58m, wallet.PostedBalance);
        Assert.AreEqual(2m, wallet.PendingOut);
    }

    // The check looks again at each wallet it finds, under the wallet's lock: a wallet put right between the two
    // looks, as one that a write was halfway through when the first one read it, is not reported.
    [TestMethod, EveryDatabase]
    public async Task WalletPutRightMeanwhileIsNotReported(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10));
        await testWallet.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 11 WHERE \"WalletId\" = {wallet.WalletId}");

        // another call holds the wallet's lock, as a write does
        await using var holder = testWallet.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await TestWallet.LockWallet(holder, wallet);

        await using var scope = testWallet.CreateScope();
        var check = scope.Reconciliation.Reconcile(TestCt);
        await testWallet.WaitForBlockedCalls(1);
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 10 WHERE \"WalletId\" = {wallet.WalletId}");

        await transaction.CommitAsync();

        Assert.IsEmpty((await check).WalletDiscrepancies);
    }

    [TestMethod, EveryDatabase]
    public async Task TotalChangedBehindTheServiceIsFound(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10),
            TestWallet.Request(source, wallet, 4, isPending: true));

        await testWallet.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 11 WHERE \"WalletId\" = {wallet.WalletId}");

        await using var scope = testWallet.CreateScope();
        var report = await scope.Reconciliation.Reconcile(TestCt);
        var walletDiscrepancy = report.WalletDiscrepancies.Single();
        Assert.AreEqual(wallet.WalletId, walletDiscrepancy.Wallet.WalletId);
        Assert.AreEqual(11m, walletDiscrepancy.Wallet.PostedIn);
        Assert.AreEqual(10m, walletDiscrepancy.FromTransfers.PostedIn);
        Assert.AreEqual(4m, walletDiscrepancy.FromTransfers.PendingIn);
        Assert.AreEqual(10m, walletDiscrepancy.LastTransaction?.PostedBalance);
        var currency = report.CurrencyImbalances.Single();
        Assert.AreEqual(TestWallet.TenantId, currency.TenantId);
        Assert.AreEqual(TestWallet.Currency, currency.Currency);
        Assert.AreEqual(11m, currency.Totals.PostedIn);
        Assert.AreEqual(10m, currency.Totals.PostedOut);
        Assert.IsFalse(report.IsReconciled);
    }

    // On the host's connection with no transaction of the host's open, the check looks again in a transaction of its
    // own, which its query must stay in: SQL Server refuses a command left out of the connection's transaction.
    [TestMethod, EveryDatabase]
    public async Task WalletDiscrepancyIsFoundOnTheHostsConnection(WalletDatabaseProvider provider)
    {
        await using var host = await CreateTestHost(provider);
        var source = await host.Wallet.CreateSource();
        var wallet = await host.Wallet.CreateWallet("owner", 0);
        await host.Wallet.Transfer(TestWallet.Request(source, wallet, 10));
        await host.Wallet.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 11 WHERE \"WalletId\" = {wallet.WalletId}");

        await using var scope = host.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<WalletReconciliationService>()
            .Reconcile(TestCt);

        Assert.AreEqual(wallet.WalletId, report.WalletDiscrepancies.Single().Wallet.WalletId);
    }

    // Nothing in the database stops a transfer from being changed, or deleted with its wallet transactions, behind
    // the service's back (docs/design.md): this check is what finds one, as its wallets' totals no longer add up.
    [TestMethod, EveryDatabase]
    public async Task TransferChangedOrDeletedBehindTheServiceIsFound(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var changed = await testWallet.CreateWallet("changed", 0);
        var deleted = await testWallet.CreateWallet("deleted", 0);
        await testWallet.Transfer(TestWallet.Request(source, changed, 10),
            TestWallet.Request(source, deleted, 5));

        await testWallet.Execute(
            $"UPDATE gmwallet.\"Transfers\" SET \"Amount\" = 1 WHERE \"ToWalletId\" = {changed.WalletId}");

        await testWallet.Execute(
            $"DELETE FROM gmwallet.\"WalletTransactions\" WHERE \"TransferId\" IN (SELECT \"TransferId\" " +
            $"FROM gmwallet.\"Transfers\" WHERE \"ToWalletId\" = {deleted.WalletId})");

        await testWallet.Execute(
            $"DELETE FROM gmwallet.\"Transfers\" WHERE \"ToWalletId\" = {deleted.WalletId}");

        await using var scope = testWallet.CreateScope();
        var report = await scope.Reconciliation.Reconcile(TestCt);
        var found = report.WalletDiscrepancies.ToDictionary(x => x.Wallet.WalletId);
        Assert.AreSequenceEqual([source.WalletId, changed.WalletId, deleted.WalletId],
            found.Keys.ToArray(), SequenceOrder.InAnyOrder);

        Assert.AreEqual(1m, found[changed.WalletId].FromTransfers.PostedIn);
        Assert.AreEqual(0m, found[deleted.WalletId].FromTransfers.PostedIn);
        // the stored totals, which nothing changed, still add up to zero
        Assert.IsEmpty(report.CurrencyImbalances);
    }

    // A wallet transaction changed or deleted behind the service's back leaves the wallet's totals right, but its
    // statement no longer ends on the wallet's balances.
    [TestMethod, EveryDatabase]
    public async Task WalletTransactionChangedOrDeletedBehindTheServiceIsFound(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var changed = await testWallet.CreateWallet("changed", 0);
        var deleted = await testWallet.CreateWallet("deleted", 0);
        await testWallet.Transfer(TestWallet.Request(source, changed, 10),
            TestWallet.Request(source, changed, 3),
            TestWallet.Request(source, deleted, 5));

        await testWallet.Execute(
            $"UPDATE gmwallet.\"WalletTransactions\" SET \"PostedBalance\" = 1 " +
            $"WHERE \"WalletId\" = {changed.WalletId} AND \"TransferId\" = (SELECT MAX(\"TransferId\") " +
            $"FROM gmwallet.\"WalletTransactions\" WHERE \"WalletId\" = {changed.WalletId})");

        await testWallet.Execute(
            $"DELETE FROM gmwallet.\"WalletTransactions\" WHERE \"WalletId\" = {deleted.WalletId}");

        await using var scope = testWallet.CreateScope();
        var report = await scope.Reconciliation.Reconcile(TestCt);
        var found = report.WalletDiscrepancies.ToDictionary(x => x.Wallet.WalletId);
        Assert.AreSequenceEqual([changed.WalletId, deleted.WalletId], found.Keys.ToArray(),
            SequenceOrder.InAnyOrder);

        Assert.AreEqual(13m, found[changed.WalletId].FromTransfers.PostedIn);
        Assert.AreEqual(13m, found[changed.WalletId].Wallet.PostedBalance);
        Assert.AreEqual(3m, found[changed.WalletId].LastTransaction?.Amount);
        Assert.AreEqual(1m, found[changed.WalletId].LastTransaction?.PostedBalance);
        Assert.AreEqual(5m, found[deleted.WalletId].Wallet.PostedBalance);
        Assert.IsNull(found[deleted.WalletId].LastTransaction);
        Assert.IsEmpty(report.CurrencyImbalances);
    }

    // A write moves two wallets at once. In a SQL Server database that reads committed rows by locks, a sum read row by
    // row would see the wallet written last after the write and the other before it, so the check waits for the write
    // to end. PostgreSQL reads a statement's rows at one moment, as a SQL Server database that reads them by their
    // versions does, so nothing waits there.
    [TestMethod, EveryDatabase]
    public async Task CurrenciesAreAddedUpAtOneMoment(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var first = await testWallet.CreateSource();
        var second = await testWallet.CreateWallet("owner", 0);
        if (provider == WalletDatabaseProvider.SqlServer)
            await testWallet.ReadCommittedRowsByLocks();

        // a write halfway through: the money into the second wallet is written, out of the first still to come
        await using var holder = testWallet.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 5 WHERE \"WalletId\" = {second.WalletId}");

        await using var scope = testWallet.CreateScope();
        var check = scope.Reconciliation.Reconcile(TestCt);
        if (provider == WalletDatabaseProvider.SqlServer)
            await testWallet.WaitForBlockedCalls(1);
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedOut\" = 5 WHERE \"WalletId\" = {first.WalletId}");

        await transaction.CommitAsync();

        Assert.IsEmpty((await check).CurrencyImbalances);
    }
}
