namespace GrayMint.Wallet.Persistence;

internal sealed class PostgreSqlWalletDialect : WalletDialect
{
    private const string Schema = WalletDbContext.Schema;
    private const string Transfers = WalletDbContext.TransfersTable;
    private const string RowTrigger = "refuse_row_change";
    private const string TruncateTrigger = "refuse_truncate";

    public override string UtcNowSql => "now()";

    // a database's own collation compares text by its bytes for equality
    public override string? TextCollation => null;

    public override string LockAccountSql =>
        $$"""
          SELECT * FROM {{Schema}}."{{WalletDbContext.AccountsTable}}"
          WHERE "LedgerId" = {0} AND "AccountId" = {1}
          FOR UPDATE
          """;

    // A row trigger misses TRUNCATE, which has a trigger of its own.
    public override IReadOnlyList<string> GuardStatements { get; } = [
        $$"""
          CREATE OR REPLACE FUNCTION {{Schema}}.refuse_transfer_change() RETURNS trigger LANGUAGE plpgsql AS $$
          BEGIN
              RAISE EXCEPTION 'A wallet transfer is never changed or deleted: a transfer the other way puts it right.';
          END
          $$
          """,
        $"""DROP TRIGGER IF EXISTS {RowTrigger} ON {Schema}."{Transfers}" """,
        $"""
         CREATE TRIGGER {RowTrigger} BEFORE UPDATE OR DELETE ON {Schema}."{Transfers}"
             FOR EACH ROW EXECUTE FUNCTION {Schema}.refuse_transfer_change()
         """,
        $"""DROP TRIGGER IF EXISTS {TruncateTrigger} ON {Schema}."{Transfers}" """,
        $"""
         CREATE TRIGGER {TruncateTrigger} BEFORE TRUNCATE ON {Schema}."{Transfers}"
             FOR EACH STATEMENT EXECUTE FUNCTION {Schema}.refuse_transfer_change()
         """
    ];

    public override IReadOnlyList<string> Triggers { get; } = [RowTrigger, TruncateTrigger];

    public override string CountTablesSql =>
        $"""
         SELECT count(*)::integer AS "Value" FROM pg_class c
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = '{Schema}' AND c.relkind = 'r'
           AND c.relname IN ('{WalletDbContext.AccountsTable}', '{Transfers}')
         """;

    // on for every session: 'O' or 'A'; one switched to 'R' fires only where a replica applies changes
    public override string CountEnabledTriggersSql =>
        $"""
         SELECT count(*)::integer AS "Value" FROM pg_trigger t
         JOIN pg_class c ON c.oid = t.tgrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = '{Schema}' AND c.relname = '{Transfers}' AND t.tgname IN ({ToSqlList(Triggers)})
           AND t.tgenabled IN ('O', 'A')
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
