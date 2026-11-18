using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GrayMint.Wallet.Persistence;

// What the two databases the wallet runs on say differently: a row lock, the time a new row gets, and how text
// compares. Everything else goes through EF, which says it for each.
internal sealed class WalletDialect(string utcNowSql, string? textCollation, string lockAccountSql)
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly WalletDialect PostgreSql = new(
        utcNowSql: "now()",
        // a database's own collation compares text by its bytes for equality
        textCollation: null,
        lockAccountSql: $$"""
            SELECT * FROM {{WalletDbContext.Schema}}."{{WalletDbContext.AccountsTable}}"
            WHERE "LedgerId" = {0} AND "AccountId" = {1}
            FOR UPDATE
            """);

    private static readonly WalletDialect SqlServer = new(
        utcNowSql: "SYSUTCDATETIME()",
        // A database's own collation is mostly blind to case, and would make "Key" and "key" one key.
        textCollation: "Latin1_General_100_BIN2",
        // UPDLOCK is held until the transaction ends, ROWLOCK keeps it to the one row
        lockAccountSql: $$"""
            SELECT * FROM [{{WalletDbContext.Schema}}].[{{WalletDbContext.AccountsTable}}] WITH (UPDLOCK, ROWLOCK)
            WHERE [LedgerId] = {0} AND [AccountId] = {1}
            """);

    // the time the database writes on a new row, in UTC
    public string UtcNowSql { get; } = utcNowSql;

    // How text compares in the wallet's columns, or null for the database's own. A key or an owner differs from
    // another by any character, its case too, on every database.
    public string? TextCollation { get; } = textCollation;

    // One account's row, with a lock that waits for another call's and is held until the transaction ends: {0} is
    // the ledger, {1} the account.
    public string LockAccountSql { get; } = lockAccountSql;

    public static WalletDialect For(DatabaseFacade database)
    {
        return database.ProviderName switch {
            PostgreSqlProvider => PostgreSql,
            SqlServerProvider => SqlServer,
            _ => throw new NotSupportedException(
                $"The wallet runs on PostgreSQL and SQL Server, and its context was given {database.ProviderName}.")
        };
    }
}
