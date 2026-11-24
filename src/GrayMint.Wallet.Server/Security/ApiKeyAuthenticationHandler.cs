using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GrayMint.Wallet.Server.Security;

// The server's one caller, the system, sends one of WalletServer:ApiKeys as "Authorization: Bearer <key>". A key is
// compared by its hash, in fixed time and with every key, so how long a call takes tells nothing of the keys.
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<WalletServerOptions> serverOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string BearerPrefix = "Bearer ";

    private readonly IReadOnlyList<ReadOnlyMemory<byte>> _keyHashes = serverOptions.Value.ApiKeys.Select(Hash).ToArray();

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;
        if (authorization == null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var hash = Hash(authorization[BearerPrefix.Length..].Trim());

        // | and never Any(): every key is compared, so the time tells nothing of which one matched
        var isServersKey = _keyHashes.Aggregate(false,
            (isFound, keyHash) => isFound | CryptographicOperations.FixedTimeEquals(hash.Span, keyHash.Span));

        if (!isServersKey)
            return Task.FromResult(AuthenticateResult.Fail("The API key is not one of the server's."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "system")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static ReadOnlyMemory<byte> Hash(string apiKey)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
    }
}
