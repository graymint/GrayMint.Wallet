using System.Data.Common;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet.Test.Tests;

// What the database itself holds, whatever the code does: tried behind the service's back, as the tables' owner.
[TestClass]
public class DatabaseGuardTest : TestBase
{
    private const string SchemaVersionSql =
        "SELECT \"SchemaVersion\" FROM gmwallet.\"Metadata\"";

    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsAWalletToItsLimit(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var wallet = await testWallet.CreateWallet("owner", -5);

        // money pending out counts, and money pending in is not there to spend
        foreach (var totals in new[] {
                     "\"PostedOut\" = 5.01", "\"PendingOut\" = 6", "\"PostedOut\" = 5.01, \"PendingIn\" = 100"
                 }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                $"UPDATE gmwallet.\"Wallets\" SET {totals} WHERE \"WalletId\" = {wallet.WalletId}"), totals);

            Assert.Contains(WalletDbContext.WalletMinAvailableBalanceCheck, exception.Message, totals);
        }

        // no total below 0, each on its own, with no limit too
        foreach (var total in new[] { "PendingOut", "PostedOut", "PendingIn", "PostedIn" }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                $"UPDATE gmwallet.\"Wallets\" SET \"{total}\" = -1, \"MinAvailableBalance\" = NULL " +
                $"WHERE \"WalletId\" = {wallet.WalletId}"), total);

            Assert.Contains(WalletDbContext.WalletTotalsCheck, exception.Message, total);
        }

        await testWallet.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedOut\" = 5 WHERE \"WalletId\" = {wallet.WalletId}");
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseKeepsATransferInItsTenantAndCurrency(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var otherTenant = await testWallet.CreateWallet("owner", null, tenantId: "2");
        var otherCurrency = await testWallet.CreateWallet("owner", null, currency: "EUR");
        var other = await testWallet.CreateWallet("other", null);

        // on either side
        foreach (var walletId in new[] { otherTenant.WalletId, otherCurrency.WalletId }) {
            var toOther = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(source.WalletId, walletId, 1)));

            Assert.Contains("FK_Transfers_Wallets_TenantId_Currency_ToWalletId", toOther.Message);
            var fromOther = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(walletId, other.WalletId, 1)));

            Assert.Contains("FK_Transfers_Wallets_TenantId_Currency_FromWalletId", fromOther.Message);
        }

        // a settlement without its pending transfer
        var noPending = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            "INSERT INTO gmwallet.\"Transfers\" (\"TenantId\", \"Currency\", \"Type\", \"ExternalReferenceKey\", \"FromWalletId\", " +
            $"\"ToWalletId\", \"Amount\") VALUES (1, 'USD', 'PostPending', NULL, {source.WalletId}, {other.WalletId}, 1)"));

        Assert.Contains(WalletDbContext.TransferTypeCheck, noPending.Message);
    }

    // A wallet transaction names its wallet and its transfer with its own tenant, so a statement never shows
    // another tenant's.
    [TestMethod, EveryDatabase]
    public async Task DatabaseKeepsAWalletTransactionInItsTenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", null);
        var otherTenant = await testWallet.CreateWallet("owner", null, tenantId: "2");
        var transfer = (await testWallet.Transfer(TestWallet.Request(source, wallet, 1)))[0];

        var walletOfAnother = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertWalletTransaction("1", otherTenant.WalletId, transfer.TransferId)));

        Assert.Contains("FK_WalletTransactions_Wallets_TenantId_WalletId", walletOfAnother.Message);
        var transferOfAnother = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertWalletTransaction("2", otherTenant.WalletId, transfer.TransferId)));

        Assert.Contains("FK_WalletTransactions_Transfers_TenantId_TransferId", transferOfAnother.Message);
        Assert.AreEqual(2L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"WalletTransactions\""));
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsATransferToAnAmountAndTwoWallets(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", null);

        foreach (var amount in new[] { 0, -1 }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
                InsertTransfer(source.WalletId, wallet.WalletId, amount)));

            Assert.Contains(WalletDbContext.TransferAmountCheck, exception.Message);
        }

        var oneWallet = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertTransfer(wallet.WalletId, wallet.WalletId, 1)));

        Assert.Contains(WalletDbContext.TransferWalletsCheck, oneWallet.Message);
    }

    // a post or a void settles a transfer of its own tenant, and a pending transfer is settled once
    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsOneSettlementOfATransferOfItsTenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", null);
        var source2 = await testWallet.CreateSource(tenantId: "2");
        var wallet2 = await testWallet.CreateWallet("owner", null, tenantId: "2");
        var held = await testWallet.Transfer(TestWallet.Request(source, wallet, 1, isPending: true, externalReferenceKey: "settled"),
            TestWallet.Request(source, wallet, 1, isPending: true, externalReferenceKey: "open"));

        await using var scope = testWallet.CreateScope();
        await scope.Transfers.PostPending(TestWallet.TenantId, ["settled"], TestCt);

        var twice = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertSettlement(TestWallet.TenantId, source.WalletId, wallet.WalletId, held[0].TransferId)));

        Assert.Contains("IX_Transfers_PendingTransferId", twice.Message);

        var otherTenant = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            InsertSettlement("2", source2.WalletId, wallet2.WalletId, held[1].TransferId)));

        Assert.Contains("FK_Transfers_Transfers_TenantId_PendingTransferId", otherTenant.Message);
    }

    // A host walls tenants apart on the tables WalletSchema.TenantTables names: a table with a tenant left out of it
    // would be open to every tenant.
    [TestMethod, EveryDatabase]
    public async Task TenantTablesAreEveryTableWithATenant(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        const string withTenant = "SELECT count(*) FROM INFORMATION_SCHEMA.COLUMNS " +
                                  "WHERE TABLE_SCHEMA = 'gmwallet' AND COLUMN_NAME = 'TenantId'";

        Assert.AreEqual(WalletSchema.TenantTables.Count, await testWallet.Count(withTenant));
        foreach (var table in WalletSchema.TenantTables)
            Assert.AreEqual(1L, await testWallet.Count($"{withTenant} AND TABLE_NAME = '{table}'"),
                table);
    }

    // Tables the setup didn't make as they are, such as tables made before the wallet kept a version: it would only
    // make things worse.
    [TestMethod, EveryDatabase]
    public async Task SetupRefusesADatabaseWithSomeOfTheTables(WalletDatabaseProvider provider)
    {
        foreach (var table in new[] { WalletSchema.WalletTransactionsTable, WalletSchema.MetadataTable }) {
            await using var testWallet = await CreateTestWallet(provider);
            await testWallet.Execute($"DROP TABLE gmwallet.\"{table}\"");

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                testWallet.Setup(), table);

            Assert.Contains("Only some of the wallet's tables", exception.Message, table);
            Assert.AreEqual(0L, await testWallet.Count(
                $"SELECT count(*) FROM information_schema.tables WHERE table_schema = 'gmwallet' AND table_name = '{table}'"), table);
        }
    }

    // A key in another script keeps its letters only in UTF-8: the setup makes no table in a database that keeps text
    // otherwise, and a host doesn't start on one.
    [TestMethod, EveryDatabase]
    public async Task WalletRefusesADatabaseThatDoesNotKeepUtf8(WalletDatabaseProvider provider)
    {
        await using var testWallet = CreateEmptyTestWallet(provider);
        await testWallet.CreateDatabaseWithoutUtf8();
        var refusal = provider == WalletDatabaseProvider.PostgreSql
            ? "keeps text in the encoding LATIN1, not in UTF-8"
            : "keeps text in the collation SQL_Latin1_General_CP1_CI_AS, not in UTF-8";

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            testWallet.Setup());

        Assert.Contains(refusal, exception.Message);
        Assert.AreEqual(0L, await testWallet.Count(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'gmwallet'"));

        using var host = BuildHost(testWallet);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains(refusal, exception.Message);
    }

    // SQL Server names a constraint at random unless told its name, and a setup step drops or changes a default by
    // its name: there every constraint has the name EF gives it, a default DF_<table>_<column>. PostgreSQL names no
    // default, and has the same six.
    [TestMethod, EveryDatabase]
    public async Task DefaultsAreNamedByTheirTableAndColumn(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        (string Table, string Column)[] defaults = [
            ("Transfers", "CreatedTime"), ("Wallets", "CreatedTime"), ("Wallets", "PendingIn"),
            ("Wallets", "PendingOut"), ("Wallets", "PostedIn"), ("Wallets", "PostedOut")
        ];

        if (provider == WalletDatabaseProvider.SqlServer) {
            Assert.AreEqual(0L, await testWallet.Count(
                "SELECT (SELECT COUNT(*) FROM sys.default_constraints WHERE is_system_named = 1) + " +
                "(SELECT COUNT(*) FROM sys.check_constraints WHERE is_system_named = 1) + " +
                "(SELECT COUNT(*) FROM sys.key_constraints WHERE is_system_named = 1) + " +
                "(SELECT COUNT(*) FROM sys.foreign_keys WHERE is_system_named = 1)"));

            Assert.AreEqual(defaults.Length, await testWallet.Count(
                "SELECT COUNT(*) FROM sys.default_constraints"));

            foreach (var (table, column) in defaults)
                Assert.AreEqual(1L, await testWallet.Count(
                    "SELECT COUNT(*) FROM sys.default_constraints AS d JOIN sys.columns AS c " +
                    "ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id " +
                    $"WHERE d.name = 'DF_{table}_{column}' AND c.name = '{column}' " +
                    $"AND OBJECT_NAME(d.parent_object_id) = '{table}'"), $"{table}.{column}");

            return;
        }

        Assert.AreEqual(defaults.Length, await testWallet.Count(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_schema = 'gmwallet' AND column_default IS NOT NULL"));

        foreach (var (table, column) in defaults)
            Assert.AreEqual(1L, await testWallet.Count(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'gmwallet' " +
                $"AND table_name = '{table}' AND column_name = '{column}' AND column_default IS NOT NULL"), $"{table}.{column}");
    }

    // one row, so the start reads one version, whatever writes to the table
    [TestMethod, EveryDatabase]
    public async Task MetadataKeepsOneRow(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);

        var exception = await Assert.ThrowsAsync<DbException>(() => testWallet.Execute(
            "INSERT INTO gmwallet.\"Metadata\" (\"MetadataId\", \"SchemaVersion\") VALUES (2, 1)"));

        Assert.Contains(WalletDbContext.MetadataOneRowCheck, exception.Message);
        Assert.AreEqual(1L, await testWallet.Count("SELECT count(*) FROM gmwallet.\"Metadata\""));
    }

    // a host that never ran the setup stops at its start, not at its first call
    [TestMethod, EveryDatabase]
    public async Task HostDoesNotStartOnADatabaseWithoutTheTables(WalletDatabaseProvider provider)
    {
        await using var withoutTable = await CreateTestWallet(provider);
        await withoutTable.Execute("DROP TABLE gmwallet.\"WalletTransactions\"");
        using var refused = BuildHost(withoutTable);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            refused.StartAsync());

        Assert.Contains("UseGrayMintWallet", exception.Message);

        await using var complete = await CreateTestWallet(provider);
        using var started = BuildHost(complete);
        await started.StartAsync();
        await started.StopAsync();
    }

    // A build of the wallet on tables of another version would read them wrong: neither its start nor its setup takes
    // them, nor tables that lost their version.
    [TestMethod, EveryDatabase]
    public async Task HostDoesNotStartOnTablesOfAnotherVersion(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        Assert.AreEqual(1L, await testWallet.Count(SchemaVersionSql));

        await testWallet.Execute("UPDATE gmwallet.\"Metadata\" SET \"SchemaVersion\" = 2");
        using var newer = BuildHost(testWallet);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            newer.StartAsync());

        Assert.Contains("of version 2", exception.Message);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => testWallet.Setup());
        Assert.Contains("of version 2", exception.Message);
        Assert.AreEqual(2L, await testWallet.Count(SchemaVersionSql));

        await testWallet.Execute("DELETE FROM gmwallet.\"Metadata\"");
        using var none = BuildHost(testWallet);
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => none.StartAsync());
        Assert.Contains("of version none", exception.Message);
    }

    // A strategy that retries refuses the transactions the wallet's calls run in, so every write would fail:
    // neither the setup nor the start takes such a context.
    [TestMethod, EveryDatabase]
    public async Task WalletRefusesAContextThatRetriesOnFailure(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var services = new ServiceCollection();
        services.AddGrayMintWallet();
        // the provider that calls it is disposed before testWallet
        // ReSharper disable once AccessToDisposedClosure
        services.AddGrayMintWalletDb(x => testWallet.UseDatabaseRetrying(x));
        await using var serviceProvider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            serviceProvider.UseGrayMintWallet(TestCt));

        Assert.Contains("EnableRetryOnFailure", exception.Message);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGrayMintWallet();
        // the host that calls it is disposed before testWallet
        // ReSharper disable once AccessToDisposedClosure
        builder.Services.AddGrayMintWalletDb(x => testWallet.UseDatabaseRetrying(x));
        using var host = builder.Build();
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());
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
