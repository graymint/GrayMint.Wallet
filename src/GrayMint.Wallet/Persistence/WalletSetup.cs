using GrayMint.Wallet.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrayMint.Wallet.Persistence;

// Makes the wallet's tables when they are not there, any number of times; and refuses a start on a database without
// them, with tables of another version, or that doesn't keep text as UTF-8.
internal static class WalletSetup
{
    private const int TableCount = 4;

    // INFORMATION_SCHEMA is on both databases, and lists only the tables the login has a right on. Its names are in
    // upper case, as SQL Server has them: a database whose collation tells case apart wouldn't find them in lower
    // case, and PostgreSQL folds them to lower case either way.
    private const string CountTablesSql =
        $"""
         SELECT CAST(COUNT(*) AS int) AS "Value" FROM INFORMATION_SCHEMA.TABLES
         WHERE TABLE_SCHEMA = '{WalletSchema.Name}' AND TABLE_TYPE = 'BASE TABLE'
           AND TABLE_NAME IN ('{WalletSchema.WalletsTable}', '{WalletSchema.TransfersTable}',
                              '{WalletSchema.WalletTransactionsTable}', '{WalletSchema.MetadataTable}')
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

        await CreateTables(walletDbContext, hostDatabase, cancellationToken);
        await ThrowIfNotReady(walletDbContext, hostDatabase, cancellationToken);
    }

    // So a host that never ran the setup stops at its start, not at its first call; and so does a build of the
    // wallet on tables of another shape, which its calls would read wrong.
    public static async Task ThrowIfNotReady(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        CancellationToken cancellationToken)
    {
        ThrowIfRetrying(walletDbContext);
        await using var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: false,
            cancellationToken);

        await ThrowIfNotUtf8(walletDbContext, cancellationToken);
        if (await CountTables(walletDbContext, cancellationToken) != TableCount)
            throw new InvalidOperationException(
                $"The wallet's tables are not in the schema {WalletSchema.Name}, or this login has no right on " +
                "them. The wallet's setup (UseGrayMintWallet) makes them.");

        var versions = await walletDbContext.Metadata
            .AsNoTracking()
            .Select(x => x.SchemaVersion)
            .ToArrayAsync(cancellationToken);

        if (versions is not [WalletSchema.Version])
            throw new InvalidOperationException(
                $"The wallet's tables in the schema {WalletSchema.Name} are of version " +
                $"{(versions.Length == 0 ? "none" : string.Join(" and ", versions))}, and this build of the wallet " +
                $"works on version {WalletSchema.Version}.");
    }

    // The tables and their version are made together, or neither, when none of the tables is there; in a session of
    // its own, over before ThrowIfNotReady begins one.
    private static async Task CreateTables(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        CancellationToken cancellationToken)
    {
        await using var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: true,
            cancellationToken);

        await ThrowIfNotUtf8(walletDbContext, cancellationToken);
        var tableCount = await CountTables(walletDbContext, cancellationToken);
        if (tableCount == 0) {
            await walletDbContext.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
            walletDbContext.Metadata.Add(new MetadataModel {
                MetadataId = WalletDbContext.MetadataRowId, SchemaVersion = WalletSchema.Version
            });

            await walletDbContext.SaveChangesAsync(cancellationToken);
        }
        else if (tableCount != TableCount)
            throw new InvalidOperationException(
                $"Only some of the wallet's tables are in the schema {WalletSchema.Name}, and the setup " +
                "doesn't know how they came to be so. It changed nothing.");

        await session.Commit(cancellationToken);
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

    // A key in another script keeps its letters only in UTF-8: elsewhere they could arrive as '?', and two keys be one.
    private static async Task ThrowIfNotUtf8(WalletDbContext walletDbContext, CancellationToken cancellationToken)
    {
        var encoding = await walletDbContext.Database
            .SqlQueryRaw<string>(WalletDialect.For(walletDbContext.Database).DatabaseEncodingSql)
            .SingleAsync(cancellationToken);

        if (encoding != "UTF-8")
            throw new InvalidOperationException(
                $"The wallet's database keeps text in {encoding}, not in UTF-8, and a key in another script would " +
                "lose its letters. Make it UTF-8: on SQL Server a default collation such as " +
                "Latin1_General_100_CI_AS_SC_UTF8, on PostgreSQL the encoding UTF8.");
    }

    private static Task<int> CountTables(WalletDbContext walletDbContext, CancellationToken cancellationToken)
    {
        return walletDbContext.Database.SqlQueryRaw<int>(CountTablesSql).SingleAsync(cancellationToken);
    }
}
