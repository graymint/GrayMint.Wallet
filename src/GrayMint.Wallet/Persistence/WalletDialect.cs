using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GrayMint.Wallet.Persistence;

// What the two databases the wallet runs on say differently: a row lock, and where the database lists the wallet's
// tables and checks. Everything else goes through EF, which says it for each.
internal abstract class WalletDialect
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly WalletDialect PostgreSql = new PostgreSqlWalletDialect();
    private static readonly WalletDialect SqlServer = new SqlServerWalletDialect();

    // the time the database writes on a new row, in UTC
    public abstract string UtcNowSql { get; }

    // How text compares in the wallet's columns, or null for the database's own. A key or an owner differs from
    // another by any character, its case too, on every database.
    public abstract string? TextCollation { get; }

    // One account's row, with a lock that waits for another call's and is held until the transaction ends: {0} is
    // the ledger, {1} the account.
    public abstract string LockAccountSql { get; }

    // each gives one count as "Value"
    public abstract string CountTablesSql { get; }
    public abstract string CountTrustedChecksSql { get; }

    public static WalletDialect For(DatabaseFacade database)
    {
        return database.ProviderName switch {
            PostgreSqlProvider => PostgreSql,
            SqlServerProvider => SqlServer,
            _ => throw new NotSupportedException(
                $"The wallet runs on PostgreSQL and SQL Server, and its context was given {database.ProviderName}.")
        };
    }

    // the names as the IN list of a query
    protected static string ToSqlList(IReadOnlyList<string> names)
    {
        return string.Join(", ", names.Select(x => $"'{x}'"));
    }
}
