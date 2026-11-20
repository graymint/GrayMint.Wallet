namespace GrayMint.Wallet.Persistence.Models;

// The one row of what the wallet keeps about its own tables, a column each: the version of their shape
// (WalletSchema.Version). The start reads only the columns it knows, so a later column changes nothing for it.
internal sealed class MetadataModel
{
    // always WalletDbContext.MetadataRowId, which the database holds it to
    public required int MetadataId { get; init; }
    public required int SchemaVersion { get; init; }
}
