using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GrayMint.Wallet.Persistence;

// What the two databases the wallet runs on say differently: a row lock, a read of many rows at one moment, the time
// a new row gets, and how text compares. Everything else goes through EF, which says it for each.
internal sealed class WalletDialect(
    string utcNowSql,
    string? textCollation,
    string lockWalletSql,
    string walletsAtOneMomentSql)
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly WalletDialect PostgreSql = new(
        utcNowSql: "now()",
        // a database's own collation compares text by its bytes for equality
        textCollation: null,
        lockWalletSql: $$"""
            SELECT * FROM {{WalletSchema.Name}}."{{WalletSchema.WalletsTable}}"
            WHERE "TenantId" = {0} AND "WalletId" = {1}
            FOR UPDATE
            """,
        // a statement reads every row as it was when the statement began
        walletsAtOneMomentSql: $$"""
            SELECT * FROM {{WalletSchema.Name}}."{{WalletSchema.WalletsTable}}"
            """);

    private static readonly WalletDialect SqlServer = new(
        utcNowSql: "SYSUTCDATETIME()",
        // A database's own collation is mostly blind to case, and would make "Order-1" and "order-1" one key.
        textCollation: "Latin1_General_100_BIN2",
        // UPDLOCK is held until the transaction ends, ROWLOCK keeps it to the one row
        lockWalletSql: $$"""
            SELECT * FROM [{{WalletSchema.Name}}].[{{WalletSchema.WalletsTable}}] WITH (UPDLOCK, ROWLOCK)
            WHERE [TenantId] = {0} AND [WalletId] = {1}
            """,
        // Row by row, unless the database reads committed rows by their versions, a read would see one wallet of a
        // transfer before the write and the other after it. TABLOCK's shared lock of the table waits for the writes
        // under way, and holds new ones off until the statement ends.
        walletsAtOneMomentSql: $$"""
            SELECT * FROM [{{WalletSchema.Name}}].[{{WalletSchema.WalletsTable}}] WITH (TABLOCK)
            """);

    // the time the database writes on a new row, in UTC
    public string UtcNowSql { get; } = utcNowSql;

    // How text compares in the wallet's columns, or null for the database's own. A key or an owner differs from
    // another by any character, its case too, on every database.
    public string? TextCollation { get; } = textCollation;

    // One wallet's row, with a lock that waits for another call's and is held until the transaction ends: {0} is
    // the tenant, {1} the wallet.
    public string LockWalletSql { get; } = lockWalletSql;

    // Every wallet's row as it was at one moment, for a sum over many: a write moves two wallets at once, and a sum
    // that read one before it and the other after would not add up.
    public string WalletsAtOneMomentSql { get; } = walletsAtOneMomentSql;

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
