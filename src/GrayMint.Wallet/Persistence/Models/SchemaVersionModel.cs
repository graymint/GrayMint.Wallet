namespace GrayMint.Wallet.Persistence.Models;

// The one row that says which shape the wallet's tables have (WalletSchema.Version).
internal sealed class SchemaVersionModel
{
    public required int Version { get; init; }
}
