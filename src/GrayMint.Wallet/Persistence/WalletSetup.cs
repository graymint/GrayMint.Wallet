using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrayMint.Wallet.Persistence;

// Makes the wallet's tables when they are not there, any number of times; and refuses a start on a database where a
// table, or a check of theirs, is missing or off.
internal static class WalletSetup
{
    private const int TableCount = 2;

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

        var dialect = WalletDialect.For(walletDbContext.Database);
        await using (var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: true,
                         cancellationToken)) {
            var tableCount = await Count(walletDbContext, dialect.CountTablesSql, cancellationToken);
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

    // A database whose checks were dropped or switched off would hold the money without them and say nothing.
    public static async Task ThrowIfIncomplete(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        CancellationToken cancellationToken)
    {
        ThrowIfRetrying(walletDbContext);
        var dialect = WalletDialect.For(walletDbContext.Database);
        await using var session = await WalletSession.Begin(walletDbContext, hostDatabase, isWrite: false,
            cancellationToken);

        if (await Count(walletDbContext, dialect.CountTablesSql, cancellationToken) != TableCount)
            throw new InvalidOperationException(
                $"The wallet's tables are not in the schema {WalletDbContext.Schema}: run the wallet's setup " +
                "(UseGrayMintWallet).");

        if (await Count(walletDbContext, dialect.CountTrustedChecksSql, cancellationToken) !=
            WalletDbContext.Checks.Count)
            throw new InvalidOperationException(
                "A check of the wallet's tables is missing, switched off or not trusted, so the database no longer " +
                "holds the money to its rules on its own.");
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

    private static Task<int> Count(WalletDbContext walletDbContext, string sql, CancellationToken cancellationToken)
    {
        return walletDbContext.Database.SqlQueryRaw<int>(sql).SingleAsync(cancellationToken);
    }
}
