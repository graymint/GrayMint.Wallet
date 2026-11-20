namespace GrayMint.Wallet;

/// <summary>
/// Where the wallet keeps its rows: tables of its own, in a schema of their own. A host that puts a wall of its own on
/// them, such as a row-level security policy by tenant, names them from here.
/// </summary>
public static class WalletSchema
{
    /// <summary>The database schema that holds the wallet's tables.</summary>
    public const string Name = "gmwallet";

    /// <summary>The wallets, each of one tenant, by its <c>TenantId</c> column.</summary>
    public const string WalletsTable = "Wallets";

    /// <summary>The transfers, each of one tenant, by its <c>TenantId</c> column.</summary>
    public const string TransfersTable = "Transfers";

    /// <summary>
    /// The wallet transactions, two for each transfer, one on each of its wallets; each of one tenant, by its
    /// <c>TenantId</c> column.
    /// </summary>
    public const string WalletTransactionsTable = "WalletTransactions";

    /// <summary>
    /// One row of what the wallet keeps about its own tables, a column each, such as <c>SchemaVersion</c>, the
    /// version of their shape: the setup writes it, and the start reads it.
    /// </summary>
    public const string MetadataTable = "Metadata";

    /// <summary>The tables whose every row belongs to one tenant, by its <c>TenantId</c> column.</summary>
    public static IReadOnlyList<string> TenantTables { get; } = [WalletsTable, TransfersTable, WalletTransactionsTable];

    // The shape the model gives the tables. A change to them raises it, with a step of the setup that takes tables of
    // the version before to it; the start refuses tables of any other version.
    internal const int Version = 1;
}
