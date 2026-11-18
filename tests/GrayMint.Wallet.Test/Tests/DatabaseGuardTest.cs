using System.Data.Common;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet.Test.Tests;

// What the database itself holds, whatever the code does: tried behind the service's back, as the tables' owner.
[TestClass]
public class DatabaseGuardTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task TransferIsNeverChangedDeletedOrEmptied(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 10));

        // SQL Server has no trigger for TRUNCATE TABLE, which needs the right to alter the table (docs/deployment.md)
        var statements = new List<string> {
            "UPDATE gmwallet.\"Transfers\" SET \"Amount\" = 1",
            "DELETE FROM gmwallet.\"Transfers\""
        };
        if (provider == WalletDatabaseProvider.PostgreSql)
            statements.Add("TRUNCATE gmwallet.\"Transfers\" CASCADE");

        foreach (var sql in statements) {
            var exception = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(sql, CancellationToken), sql);
            Assert.Contains("never changed or deleted", exception.Message);
        }

        Assert.AreEqual(1L, await wallet.Count("SELECT count(*) FROM gmwallet.\"Transfers\" WHERE \"Amount\" = 10", CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseHoldsAnAccountToItsLimit(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await wallet.CreateAccount("owner", -5, CancellationToken);

        var exception = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"DebitsPosted\" = 5.01 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken));
        Assert.Contains(WalletDbContext.AccountMinBalanceCheck, exception.Message);

        // a pending debit counts
        exception = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"DebitsPending\" = 6 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken));
        Assert.Contains(WalletDbContext.AccountMinBalanceCheck, exception.Message);

        exception = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"CreditsPosted\" = -1, \"MinBalance\" = NULL WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken));
        Assert.Contains(WalletDbContext.AccountTotalsCheck, exception.Message);

        await wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"DebitsPosted\" = 5 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken);
    }

    [TestMethod, EveryDatabase]
    public async Task DatabaseKeepsATransferInItsLedgerAndCurrency(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var otherLedger = await wallet.CreateAccount("owner", null, CancellationToken, ledgerId: 2);
        var otherCurrency = await wallet.CreateAccount("owner", null, CancellationToken, currency: "EUR");
        var other = await wallet.CreateAccount("other", null, CancellationToken);

        foreach (var creditAccountId in new[] { otherLedger.AccountId, otherCurrency.AccountId }) {
            var exception = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(
                "INSERT INTO gmwallet.\"Transfers\" (\"LedgerId\", \"Currency\", \"Type\", \"Key\", \"DebitAccountId\", " +
                $"\"CreditAccountId\", \"Amount\") VALUES (1, 'USD', 'Posted', 'k', {source.AccountId}, {creditAccountId}, 1)",
                CancellationToken));
            Assert.Contains("FK_Transfers_Accounts_LedgerId_Currency_CreditAccountId", exception.Message);
        }

        // a settlement without its pending transfer
        var noPending = await Assert.ThrowsAsync<DbException>(() => wallet.Execute(
            "INSERT INTO gmwallet.\"Transfers\" (\"LedgerId\", \"Currency\", \"Type\", \"Key\", \"DebitAccountId\", " +
            $"\"CreditAccountId\", \"Amount\") VALUES (1, 'USD', 'PostPending', NULL, {source.AccountId}, {other.AccountId}, 1)",
            CancellationToken));
        Assert.Contains(WalletDbContext.TransferTypeCheck, noPending.Message);
    }

    [TestMethod, EveryDatabase]
    public async Task WalletRefusesADatabaseWithoutItsGuards(WalletDatabaseProvider provider)
    {
        await using (var complete = await TestWallet.Create(provider, CancellationToken)) {
            await using var completeDbContext = complete.CreateDbContext();
            await WalletSetup.ThrowIfIncomplete(completeDbContext, null, CancellationToken);
        }

        foreach (var sql in SwitchOffs(provider)) {
            await using var wallet = await TestWallet.Create(provider, CancellationToken);
            await wallet.Execute(sql, CancellationToken);
            await using var dbContext = wallet.CreateDbContext();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                WalletSetup.ThrowIfIncomplete(dbContext, null, CancellationToken), sql);
        }
    }

    // the setup can run again, and puts back a trigger that was dropped or switched off
    [TestMethod, EveryDatabase]
    public async Task SetupPutsTheTriggersBack(WalletDatabaseProvider provider)
    {
        var triggerSwitchOffs = SwitchOffs(provider).Where(x => x.Contains("TRIGGER")).ToArray();
        Assert.IsNotEmpty(triggerSwitchOffs);
        foreach (var sql in triggerSwitchOffs) {
            await using var wallet = await TestWallet.Create(provider, CancellationToken);
            await wallet.Execute(sql, CancellationToken);
            await using var dbContext = wallet.CreateDbContext();
            await WalletSetup.Run(dbContext, null, CancellationToken);
            await WalletSetup.ThrowIfIncomplete(dbContext, null, CancellationToken);
        }
    }

    // tables the setup didn't make as they are: it would only make things worse
    [TestMethod, EveryDatabase]
    public async Task SetupRefusesADatabaseWithSomeOfTheTables(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        await wallet.Execute("DROP TABLE gmwallet.\"Transfers\"", CancellationToken);

        await using var dbContext = wallet.CreateDbContext();
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            WalletSetup.Run(dbContext, null, CancellationToken));
        Assert.Contains("Only some of the wallet's tables", exception.Message);
        Assert.AreEqual(0L, await wallet.Count(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'gmwallet' AND table_name = 'Transfers'",
            CancellationToken));
    }

    [TestMethod, EveryDatabase]
    public async Task HostDoesNotStartOnADatabaseWithoutTheGuards(WalletDatabaseProvider provider)
    {
        await using var withoutGuards = await TestWallet.CreateWithoutGuards(provider, CancellationToken);
        using var refused = BuildHost(withoutGuards);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => refused.StartAsync(CancellationToken));

        await using var complete = await TestWallet.Create(provider, CancellationToken);
        using var started = BuildHost(complete);
        await started.StartAsync(CancellationToken);
        await started.StopAsync(CancellationToken);
    }

    // the limit's check gone from the accounts, and one of its name on the transfers in its place
    private const string MovedCheck =
        "ALTER TABLE gmwallet.\"Accounts\" DROP CONSTRAINT \"CK_Accounts_MinBalance\"; " +
        "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Accounts_MinBalance\" CHECK (\"Amount\" > 0)";

    private static IReadOnlyList<string> SwitchOffs(WalletDatabaseProvider provider)
    {
        return provider == WalletDatabaseProvider.PostgreSql
            ? [
                "DROP TRIGGER refuse_row_change ON gmwallet.\"Transfers\"",
                "ALTER TABLE gmwallet.\"Transfers\" DISABLE TRIGGER refuse_truncate",
                "ALTER TABLE gmwallet.\"Transfers\" ENABLE REPLICA TRIGGER refuse_row_change",
                MovedCheck,
                "ALTER TABLE gmwallet.\"Accounts\" DROP CONSTRAINT \"CK_Accounts_MinBalance\"",
                "ALTER TABLE gmwallet.\"Transfers\" DROP CONSTRAINT \"CK_Transfers_Amount\"; " +
                "ALTER TABLE gmwallet.\"Transfers\" ADD CONSTRAINT \"CK_Transfers_Amount\" CHECK (\"Amount\" > 0) NOT VALID"
            ]
            : [
                "DROP TRIGGER gmwallet.refuse_transfer_change",
                "DISABLE TRIGGER gmwallet.refuse_transfer_change ON gmwallet.\"Transfers\"",
                "ALTER TABLE gmwallet.\"Accounts\" DROP CONSTRAINT \"CK_Accounts_MinBalance\"",
                "ALTER TABLE gmwallet.\"Transfers\" NOCHECK CONSTRAINT \"CK_Transfers_Amount\"",
                MovedCheck
            ];
    }

    // A strategy that retries refuses the transactions the wallet's calls run in, so every write would fail:
    // neither the setup nor the start takes such a context.
    [TestMethod, EveryDatabase]
    public async Task WalletRefusesAContextThatRetriesOnFailure(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var options = new DbContextOptionsBuilder<WalletDbContext>();
        wallet.UseDatabaseRetrying(options);
        await using var dbContext = new WalletDbContext(options.Options);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            WalletSetup.Run(dbContext, null, CancellationToken));
        Assert.Contains("EnableRetryOnFailure", exception.Message);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGrayMintWallet();
        builder.Services.AddGrayMintWalletDb(x => wallet.UseDatabaseRetrying(x));
        using var host = builder.Build();
        exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken));
        Assert.Contains("EnableRetryOnFailure", exception.Message);
    }

    private static IHost BuildHost(TestWallet wallet)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddGrayMintWallet();
        builder.Services.AddGrayMintWalletDb(options => wallet.UseDatabase(options));
        return builder.Build();
    }
}
