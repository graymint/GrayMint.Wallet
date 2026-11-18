using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrayMint.Wallet.Persistence;

// Makes the wallet's tables when they are not there, any number of times; and refuses a start on a database without
// them.
internal static class WalletSetup
{
    private const int TableCount = 2;

    // INFORMATION_SCHEMA is on both databases, and lists only the tables the login has a right on. Its names are in
    // upper case, as SQL Server has them: a database whose collation tells case apart wouldn't find them in lower
    // case, and PostgreSQL folds them to lower case either way.
    private const string CountTablesSql =
        $"""
         SELECT CAST(COUNT(*) AS int) AS "Value" FROM INFORMATION_SCHEMA.TABLES
         WHERE TABLE_SCHEMA = '{WalletDbContext.Schema}' AND TABLE_TYPE = 'BASE TABLE'
           AND TABLE_NAME IN ('{WalletDbContext.AccountsTable}', '{WalletDbContext.TransfersTable}')
         """;

    public static async Task Run(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        CancellationToken cancellationToken)
    {
        ThrowIfRetrying(walletDbContext);

        // a database of the wallet's own is made here; a host's database is the host's to make
        if (hostDatabase == null) {
            var databaseCreator = walletDbContext.GetService<IRelationalDatabaseCreator>();
            if (!await databaseCreator.ExistsAsync(cancellationToken))
                await databaseCreator.CreateAsync(cancellationToken);
        }

        await using (var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: true,
                         cancellationToken)) {
            var tableCount = await CountTables(walletDbContext, cancellationToken);
            if (tableCount == 0)
                await walletDbContext.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
            else if (tableCount != TableCount)
                throw new InvalidOperationException(
                    $"Only some of the wallet's tables are in the schema {WalletDbContext.Schema}, and the setup " +
                    "doesn't know how they came to be so. It changed nothing.");

            await session.Commit(cancellationToken);
        }

        await ThrowIfIncomplete(walletDbContext, hostDatabase, cancellationToken);
    }

    // so a host that never ran the setup stops at its start, not at its first call
    public static async Task ThrowIfIncomplete(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        CancellationToken cancellationToken)
    {
        ThrowIfRetrying(walletDbContext);
        await using var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: false,
            cancellationToken);

        if (await CountTables(walletDbContext, cancellationToken) != TableCount)
            throw new InvalidOperationException(
                $"The wallet's tables are not in the schema {WalletDbContext.Schema}, or this login has no right on " +
                "them. The wallet's setup (UseGrayMintWallet) makes them.");
    }

    // A strategy that retries on failure refuses a transaction begun outside it, so every call that writes would
    // fail. A caller retries a whole call instead, which the keys make safe.
    private static void ThrowIfRetrying(WalletDbContext walletDbContext)
    {
        if (walletDbContext.Database.CreateExecutionStrategy().RetriesOnFailure)
            throw new InvalidOperationException(
                "The wallet's context retries on failure (EnableRetryOnFailure), which refuses the transactions its " +
                "calls run in. Leave it out of the wallet's options and retry a whole call: its keys make that safe.");
    }

    private static Task<int> CountTables(WalletDbContext walletDbContext, CancellationToken cancellationToken)
    {
        return walletDbContext.Database.SqlQueryRaw<int>(CountTablesSql).SingleAsync(cancellationToken);
    }
}
