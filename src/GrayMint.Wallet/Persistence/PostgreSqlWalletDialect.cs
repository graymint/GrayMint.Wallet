namespace GrayMint.Wallet.Persistence;

internal sealed class PostgreSqlWalletDialect : WalletDialect
{
    private const string Schema = WalletDbContext.Schema;
    private const string Transfers = WalletDbContext.TransfersTable;

    public override string UtcNowSql => "now()";

    // a database's own collation compares text by its bytes for equality
    public override string? TextCollation => null;

    public override string LockAccountSql =>
        $$"""
          SELECT * FROM {{Schema}}."{{WalletDbContext.AccountsTable}}"
          WHERE "LedgerId" = {0} AND "AccountId" = {1}
          FOR UPDATE
          """;

    public override string CountTablesSql =>
        $"""
         SELECT count(*)::integer AS "Value" FROM pg_class c
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = '{Schema}' AND c.relkind = 'r'
           AND c.relname IN ('{WalletDbContext.AccountsTable}', '{Transfers}')
         """;

    public override string CountTrustedChecksSql =>
        $"""
         SELECT count(*)::integer AS "Value" FROM pg_constraint k
         JOIN pg_class c ON c.oid = k.conrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = '{Schema}' AND k.contype = 'c' AND k.convalidated
           AND ((c.relname = '{WalletDbContext.AccountsTable}' AND k.conname IN ({ToSqlList(WalletDbContext.AccountChecks)}))
             OR (c.relname = '{Transfers}' AND k.conname IN ({ToSqlList(WalletDbContext.TransferChecks)})))
         """;
}
