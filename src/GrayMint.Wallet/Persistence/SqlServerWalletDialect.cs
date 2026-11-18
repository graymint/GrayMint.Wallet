namespace GrayMint.Wallet.Persistence;

internal sealed class SqlServerWalletDialect : WalletDialect
{
    private const string Schema = WalletDbContext.Schema;
    private const string Transfers = WalletDbContext.TransfersTable;
    private const string ChangeTrigger = "refuse_transfer_change";

    public override string UtcNowSql => "SYSUTCDATETIME()";

    // A database's own collation is mostly blind to case, and would make "Key" and "key" one key.
    public override string TextCollation => "Latin1_General_100_BIN2";

    // UPDLOCK is held until the transaction ends, ROWLOCK keeps it to the one row
    public override string LockAccountSql =>
        $$"""
          SELECT * FROM [{{Schema}}].[{{WalletDbContext.AccountsTable}}] WITH (UPDLOCK, ROWLOCK)
          WHERE [LedgerId] = {0} AND [AccountId] = {1}
          """;

    // SQL Server has no trigger for TRUNCATE TABLE: emptying a table needs the right to alter it, which the
    // wallet's login never needs (docs/deployment.md).
    public override IReadOnlyList<string> GuardStatements { get; } = [
        $"""
         CREATE OR ALTER TRIGGER [{Schema}].[{ChangeTrigger}] ON [{Schema}].[{Transfers}]
         AFTER UPDATE, DELETE
         AS
         BEGIN
             SET NOCOUNT ON;
             THROW 50000, N'A wallet transfer is never changed or deleted: a transfer the other way puts it right.', 1;
         END
         """,
        $"ENABLE TRIGGER [{Schema}].[{ChangeTrigger}] ON [{Schema}].[{Transfers}]"
    ];

    public override IReadOnlyList<string> Triggers { get; } = [ChangeTrigger];

    public override string CountTablesSql =>
        $"""
         SELECT COUNT(*) AS [Value] FROM sys.tables t
         JOIN sys.schemas s ON s.schema_id = t.schema_id
         WHERE s.name = N'{Schema}' AND t.name IN (N'{WalletDbContext.AccountsTable}', N'{Transfers}')
         """;

    public override string CountEnabledTriggersSql =>
        $"""
         SELECT COUNT(*) AS [Value] FROM sys.triggers tr
         JOIN sys.tables t ON t.object_id = tr.parent_id
         JOIN sys.schemas s ON s.schema_id = t.schema_id
         WHERE s.name = N'{Schema}' AND t.name = N'{Transfers}' AND tr.name IN ({ToSqlList(Triggers)})
           AND tr.is_disabled = 0
         """;

    public override string CountTrustedChecksSql =>
        $"""
         SELECT COUNT(*) AS [Value] FROM sys.check_constraints k
         JOIN sys.tables t ON t.object_id = k.parent_object_id
         JOIN sys.schemas s ON s.schema_id = t.schema_id
         WHERE s.name = N'{Schema}' AND k.is_disabled = 0 AND k.is_not_trusted = 0
           AND ((t.name = N'{WalletDbContext.AccountsTable}' AND k.name IN ({ToSqlList(WalletDbContext.AccountChecks)}))
             OR (t.name = N'{Transfers}' AND k.name IN ({ToSqlList(WalletDbContext.TransferChecks)})))
         """;
}
