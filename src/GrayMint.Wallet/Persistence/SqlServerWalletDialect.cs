namespace GrayMint.Wallet.Persistence;

internal sealed class SqlServerWalletDialect : WalletDialect
{
    private const string Schema = WalletDbContext.Schema;
    private const string Transfers = WalletDbContext.TransfersTable;

    public override string UtcNowSql => "SYSUTCDATETIME()";

    // A database's own collation is mostly blind to case, and would make "Key" and "key" one key.
    public override string TextCollation => "Latin1_General_100_BIN2";

    // UPDLOCK is held until the transaction ends, ROWLOCK keeps it to the one row
    public override string LockAccountSql =>
        $$"""
          SELECT * FROM [{{Schema}}].[{{WalletDbContext.AccountsTable}}] WITH (UPDLOCK, ROWLOCK)
          WHERE [LedgerId] = {0} AND [AccountId] = {1}
          """;

    public override string CountTablesSql =>
        $"""
         SELECT COUNT(*) AS [Value] FROM sys.tables t
         JOIN sys.schemas s ON s.schema_id = t.schema_id
         WHERE s.name = N'{Schema}' AND t.name IN (N'{WalletDbContext.AccountsTable}', N'{Transfers}')
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
