namespace GrayMint.Wallet.Server;

public class WalletServerOptions
{
    public const string SectionName = "WalletServer";

    // the database ConnectionStrings:WalletDatabase names; no default, so a connection string is never read as the
    // other kind
    public required WalletDatabaseProvider DatabaseProvider { get; init; }

    // Every system token carries it, so changing it ends every system token at once.
    public required string SystemAuthorizationCode { get; init; }
}
