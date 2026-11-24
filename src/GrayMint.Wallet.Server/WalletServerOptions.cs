namespace GrayMint.Wallet.Server;

public class WalletServerOptions
{
    public const string SectionName = "WalletServer";

    // as many characters as 32 random bytes in base64 hold, near enough: a key a person made up is refused
    public const int MinApiKeyLength = 32;

    // the database ConnectionStrings:WalletDatabase names; no default, so a connection string is never read as the
    // other kind
    public required WalletDatabaseProvider DatabaseProvider { get; init; }

    // The keys the system calls with. A second one lets a new key take over before the old one goes.
    public IReadOnlyList<string> ApiKeys { get; init; } = [];

    public static bool IsStrongApiKey(string? apiKey)
    {
        return apiKey is { Length: >= MinApiKeyLength } && apiKey.Trim() == apiKey;
    }
}
