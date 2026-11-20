using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GrayMint.Wallet.Persistence;

// What the two databases the wallet runs on say differently: a row lock, a read of many rows at one moment, the time
// a new row gets, and how text is kept and compares. Everything else goes through EF, which says it for each.
internal sealed class WalletDialect(
    string utcNowSql,
    string? databaseCollation,
    string databaseEncodingSql,
    string? textCollation,
    int textLengthPerCharacter,
    string lockWalletSql,
    string walletsAtOneMomentSql)
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly WalletDialect PostgreSql = new(
        utcNowSql: "now()",
        // a database keeps text in the encoding it was made with, UTF-8 on any default install
        databaseCollation: null,
        databaseEncodingSql: """
            SELECT CASE WHEN pg_encoding_to_char(encoding) = 'UTF8' THEN 'UTF-8'
                        ELSE 'the encoding ' || pg_encoding_to_char(encoding) END AS "Value"
            FROM pg_database WHERE datname = current_database()
            """,
        // a database's own collation compares text by its bytes for equality
        textCollation: null,
        // text is kept in the database's encoding, UTF-8, and a length counts characters
        textLengthPerCharacter: 1,
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
        // The client sends a varchar parameter in the code page of the database's own collation, whatever the
        // column's, and a letter outside it arrives as '?': two keys would be one. A UTF-8 code page keeps them all,
        // so the start refuses a database without one (TextOfAnyScriptIsKeptWhole shows the letters kept).
        databaseCollation: "Latin1_General_100_CI_AS_SC_UTF8",
        databaseEncodingSql: """
            SELECT CASE WHEN COLLATIONPROPERTY(Name, 'CodePage') = 65001 THEN N'UTF-8'
                        ELSE N'the collation ' + Name END AS [Value]
            FROM (SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS Name) AS Setting
            """,
        // A database's own collation is mostly blind to case, and would make "Order-1" and "order-1" one key. Under a
        // UTF-8 collation varchar keeps text as UTF-8, as PostgreSQL does, a byte a character of ASCII.
        textCollation: "Latin1_General_100_BIN2_UTF8",
        // A varchar's length counts bytes, and a .NET character takes up to 3 of UTF-8: a character of most scripts
        // takes 2 or 3, and one outside them is two .NET characters, 4 bytes.
        textLengthPerCharacter: 3,
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

    // The collation of a database the setup makes, or null for the server's own. A host's database is the host's to
    // make; the start refuses one that doesn't keep text as UTF-8.
    public string? DatabaseCollation { get; } = databaseCollation;

    // "UTF-8" when the database keeps text so, or else the setting it keeps text by, as the one column Value
    public string DatabaseEncodingSql { get; } = databaseEncodingSql;

    // How text compares in the wallet's columns, or null for the database's own. A key or an owner differs from
    // another by any character, its case too, on every database.
    public string? TextCollation { get; } = textCollation;

    // One wallet's row, with a lock that waits for another call's and is held until the transaction ends: {0} is
    // the tenant, {1} the wallet.
    public string LockWalletSql { get; } = lockWalletSql;

    // Every wallet's row as it was at one moment, for a sum over many: a write moves two wallets at once, and a sum
    // that read one before it and the other after would not add up.
    public string WalletsAtOneMomentSql { get; } = walletsAtOneMomentSql;

    // the length of a text column that holds up to maxLength .NET characters
    public int TextLength(int maxLength)
    {
        return maxLength * textLengthPerCharacter;
    }

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
