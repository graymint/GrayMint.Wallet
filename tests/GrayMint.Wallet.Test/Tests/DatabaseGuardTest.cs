using System.Data.Common;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet.Test.Tests;

// What the database itself holds, whatever the code does: tried behind the service's back, as the tables' owner.
[TestClass]
public class DatabaseGuardTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsAWalletToItsLimit(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", -5, CancellationToken);

        // money pending out counts, and money pending in is not there to spend
        foreach (var totals in new[] {
                     "\"PostedOut\" = 5.01", "\"PendingOut\" = 6", "\"PostedOut\" = 5.01, \"PendingIn\" = 100"
                 }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                $"UPDATE gmwallet.\"Wallets\" SET {totals} WHERE \"WalletId\" = {wallet.WalletId}",
                CancellationToken), totals);
            Assert.Contains(WalletDbContext.WalletMinAvailableBalanceCheck, exception.Message, totals);
        }

        // no total below 0, each on its own, with no limit too
        foreach (var total in new[] { "PendingOut", "PostedOut", "PendingIn", "PostedIn" }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                $"UPDATE gmwallet.\"Wallets\" SET \"{total}\" = -1, \"MinAvailableBalance\" = NULL " +
                $"WHERE \"WalletId\" = {wallet.WalletId}", CancellationToken), total);
            Assert.Contains(WalletDbContext.WalletTotalsCheck, exception.Message, total);
        }

        await testWallet.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedOut\" = 5 WHERE \"WalletId\" = {wallet.WalletId}",
            CancellationToken);
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseKeepsATransferInItsTenantAndCurrency(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var otherTenant = await testWallet.CreateWallet("owner", null, CancellationToken, tenantId: "2");
        var otherCurrency = await testWallet.CreateWallet("owner", null, CancellationToken, currency: "EUR");
        var other = await testWallet.CreateWallet("other", null, CancellationToken);

        // on either side
        foreach (var walletId in new[] { otherTenant.WalletId, otherCurrency.WalletId }) {
            var toOther = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(source.WalletId, walletId, 1), CancellationToken));
            Assert.Contains("FK_Transfers_Wallets_TenantId_Currency_ToWalletId", toOther.Message);
            var fromOther = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(walletId, other.WalletId, 1), CancellationToken));
            Assert.Contains("FK_Transfers_Wallets_TenantId_Currency_FromWalletId", fromOther.Message);
        }

        // a settlement without its pending transfer
        var noPending = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            "INSERT INTO gmwallet.\"Transfers\" (\"TenantId\", \"Currency\", \"Type\", \"ExternalReferenceKey\", \"FromWalletId\", " +
            $"\"ToWalletId\", \"Amount\") VALUES (1, 'USD', 'PostPending', NULL, {source.WalletId}, {other.WalletId}, 1)",
            CancellationToken));
        Assert.Contains(WalletDbContext.TransferTypeCheck, noPending.Message);
    }

    // A wallet transaction names its wallet and its transfer with its own tenant, so a statement never shows
    // another tenant's.
    [TestMethod, EveryDatabase]
    public async Task DatabaseKeepsAWalletTransactionInItsTenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", null, CancellationToken);
        var otherTenant = await testWallet.CreateWallet("owner", null, CancellationToken, tenantId: "2");
        var transfer = (await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 1)))[0];

        var walletOfAnother = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertWalletTransaction("1", otherTenant.WalletId, transfer.TransferId), CancellationToken));
        Assert.Contains("FK_WalletTransactions_Wallets_TenantId_WalletId", walletOfAnother.Message);
        var transferOfAnother = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertWalletTransaction("2", otherTenant.WalletId, transfer.TransferId), CancellationToken));
        Assert.Contains("FK_WalletTransactions_Transfers_TenantId_TransferId", transferOfAnother.Message);
        Assert.AreEqual(2L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"WalletTransactions\"",
            CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsATransferToAnAmountAndTwoWallets(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", null, CancellationToken);

        foreach (var amount in new[] { 0, -1 }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(source.WalletId, wallet.WalletId, amount), CancellationToken));
            Assert.Contains(WalletDbContext.TransferAmountCheck, exception.Message);
        }

        var oneWallet = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertTransfer(wallet.WalletId, wallet.WalletId, 1), CancellationToken));
        Assert.Contains(WalletDbContext.TransferWalletsCheck, oneWallet.Message);
    }

    // a post or a void settles a transfer of its own tenant, and a pending transfer is settled once
    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsOneSettlementOfATransferOfItsTenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", null, CancellationToken);
        var source2 = await testWallet.CreateSource(CancellationToken, tenantId: "2");
        var wallet2 = await testWallet.CreateWallet("owner", null, CancellationToken, tenantId: "2");
        var held = await testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, wallet, 1, isPending: true, externalReferenceKey: "settled"),
            TestWallet.Request(source, wallet, 1, isPending: true, externalReferenceKey: "open"));
        await using (var scope = testWallet.CreateScope())
            await scope.Transfers.PostPending(TestWallet.TenantId, ["settled"], CancellationToken);

        var twice = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertSettlement(TestWallet.TenantId, source.WalletId, wallet.WalletId, held[0].TransferId),
            CancellationToken));
        Assert.Contains("IX_Transfers_PendingTransferId", twice.Message);

        var otherTenant = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertSettlement("2", source2.WalletId, wallet2.WalletId, held[1].TransferId), CancellationToken));
        Assert.Contains("FK_Transfers_Transfers_TenantId_PendingTransferId", otherTenant.Message);
    }

    // A host walls tenants apart on the tables WalletSchema.TenantTables names: a table with a tenant left out of it
    // would be open to every tenant.
    [TestMethod, EveryDatabase]
    public async Task TenantTablesAreEveryTableWithATenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        const string withTenant = "SELECT count(*) FROM INFORMATION_SCHEMA.COLUMNS " +
                                  "WHERE TABLE_SCHEMA = 'gmwallet' AND COLUMN_NAME = 'TenantId'";
        Assert.AreEqual((long)WalletSchema.TenantTables.Count, await testWallet.Count(withTenant, CancellationToken));
        foreach (var table in WalletSchema.TenantTables)
            Assert.AreEqual(1L, await testWallet.Count($"{withTenant} AND TABLE_NAME = '{table}'", CancellationToken),
                table);
    }

    // Tables the setup didn't make as they are, such as tables made before the wallet kept a version: it would only
    // make things worse.
    [TestMethod, EveryDatabase]
    public async Task SetupRefusesADatabaseWithSomeOfTheTables(WalletDatabaseProvider provider)
    {
        foreach (var table in new[] { WalletSchema.WalletTransactionsTable, WalletSchema.SchemaVersionTable }) {
            await using var testWallet = await TestWallet.Create(provider, CancellationToken);
            await testWallet.Execute($"DROP TABLE gmwallet.\"{table}\"", CancellationToken);

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                testWallet.Setup(CancellationToken), table);
            Assert.Contains("Only some of the wallet's tables", exception.Message, table);
            Assert.AreEqual(0L, await testWallet.Count(
                $"SELECT count(*) FROM information_schema.tables WHERE table_schema = 'gmwallet' AND table_name = '{table}'",
                CancellationToken), table);
        }
    }

    // A key in another script keeps its letters only in UTF-8: the setup makes no table in a database that keeps text
    // otherwise, and a host doesn't start on one.
    [TestMethod, EveryDatabase]
    public async Task WalletRefusesADatabaseThatDoesNotKeepUtf8(WalletDatabaseProvider provider)
    {
        await using var testWallet = TestWallet.CreateEmpty(provider);
        await testWallet.CreateDatabaseWithoutUtf8(CancellationToken);
        var refusal = provider == WalletDatabaseProvider.PostgreSql
            ? "keeps text in the encoding LATIN1, not in UTF-8"
            : "keeps text in the collation SQL_Latin1_General_CP1_CI_AS, not in UTF-8";

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            testWallet.Setup(CancellationToken));
        Assert.Contains(refusal, exception.Message);
        Assert.AreEqual(0L, await testWallet.Count(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'gmwallet'", CancellationToken));

        using var host = BuildHost(testWallet);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken));
        Assert.Contains(refusal, exception.Message);
    }

    // a host that never ran the setup stops at its start, not at its first call
    [TestMethod, EveryDatabase]
    public async Task HostDoesNotStartOnADatabaseWithoutTheTables(WalletDatabaseProvider provider)
    {
        await using var withoutTable = await TestWallet.Create(provider, CancellationToken);
        await withoutTable.Execute("DROP TABLE gmwallet.\"WalletTransactions\"", CancellationToken);
        using var refused = BuildHost(withoutTable);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            refused.StartAsync(CancellationToken));
        Assert.Contains("UseGrayMintWallet", exception.Message);

        await using var complete = await TestWallet.Create(provider, CancellationToken);
        using var started = BuildHost(complete);
        await started.StartAsync(CancellationToken);
        await started.StopAsync(CancellationToken);
    }

    // A build of the wallet on tables of another version would read them wrong: neither its start nor its setup takes
    // them, nor tables that lost their version.
    [TestMethod, EveryDatabase]
    public async Task HostDoesNotStartOnTablesOfAnotherVersion(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        Assert.AreEqual(1L, await testWallet.Count("SELECT \"Version\" FROM gmwallet.\"SchemaVersion\"", CancellationToken));

        await testWallet.Execute("UPDATE gmwallet.\"SchemaVersion\" SET \"Version\" = 2", CancellationToken);
        using var newer = BuildHost(testWallet);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            newer.StartAsync(CancellationToken));
        Assert.Contains("of version 2", exception.Message);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => testWallet.Setup(CancellationToken));
        Assert.Contains("of version 2", exception.Message);
        Assert.AreEqual(2L, await testWallet.Count("SELECT \"Version\" FROM gmwallet.\"SchemaVersion\"", CancellationToken));

        await testWallet.Execute("DELETE FROM gmwallet.\"SchemaVersion\"", CancellationToken);
        using var none = BuildHost(testWallet);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => none.StartAsync(CancellationToken));
        Assert.Contains("of version none", exception.Message);
    }

    // A strategy that retries refuses the transactions the wallet's calls run in, so every write would fail:
    // neither the setup nor the start takes such a context.
    [TestMethod, EveryDatabase]
    public async Task WalletRefusesAContextThatRetriesOnFailure(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var services = new ServiceCollection();
        services.AddGrayMintWallet();
        services.AddGrayMintWalletDb(x => testWallet.UseDatabaseRetrying(x));
        await using var serviceProvider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            serviceProvider.UseGrayMintWallet(CancellationToken));
        Assert.Contains("EnableRetryOnFailure", exception.Message);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGrayMintWallet();
        builder.Services.AddGrayMintWalletDb(x => testWallet.UseDatabaseRetrying(x));
        using var host = builder.Build();
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken));
        Assert.Contains("EnableRetryOnFailure", exception.Message);
    }

    // invariant, as a culture of the machine's could write a number in a way SQL doesn't read
    private static string InsertTransfer(string fromWalletId, string toWalletId, int amount)
    {
        return FormattableString.Invariant($"""
            INSERT INTO gmwallet."Transfers"
                ("TenantId", "Currency", "Type", "ExternalReferenceKey", "FromWalletId", "ToWalletId", "Amount")
            VALUES (1, 'USD', 'Posted', 'k', {fromWalletId}, {toWalletId}, {amount})
            """);
    }

    private static string InsertSettlement(string tenantId, string fromWalletId, string toWalletId,
        string pendingTransferId)
    {
        return $"""
            INSERT INTO gmwallet."Transfers"
                ("TenantId", "Currency", "Type", "ExternalReferenceKey", "FromWalletId", "ToWalletId", "Amount", "PendingTransferId")
            VALUES ({tenantId}, 'USD', 'PostPending', NULL, {fromWalletId}, {toWalletId}, 1, {pendingTransferId})
            """;
    }

    private static string InsertWalletTransaction(string tenantId, string walletId, string transferId)
    {
        return $"""
            INSERT INTO gmwallet."WalletTransactions"
                ("TenantId", "WalletId", "TransferId", "Amount", "PostedBalance", "AvailableBalance")
            VALUES ({tenantId}, {walletId}, {transferId}, 1, 1, 1)
            """;
    }

    private static IHost BuildHost(TestWallet testWallet)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGrayMintWallet();
        builder.Services.AddGrayMintWalletDb(options => testWallet.UseDatabase(options));
        return builder.Build();
    }
}
