using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.Authentications;
using GrayMint.Authorization.Authentications.Dtos;
using GrayMint.Wallet.Server;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Helper;

// The server as a deployment runs it: its setup command makes the database, then it serves, with settings of its
// own and none of Development's.
public sealed class TestWalletServer : IAsyncDisposable
{
    private readonly TestWalletFactory _factory;
    private readonly string _secret;

    public TestWallet Database { get; }

    private TestWalletServer(TestWallet database, string secret, IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? replaceServices)
    {
        Database = database;
        _secret = secret;
        _factory = new TestWalletFactory(settings, replaceServices);
    }

    public const string SystemAuthorizationCode = "test-code";

    public static async Task<TestWalletServer> Create(WalletDatabaseProvider provider,
        Action<IServiceCollection>? replaceServices = null)
    {
        var database = TestWallet.CreateEmpty(provider);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = new Dictionary<string, string?> {
            ["ConnectionStrings:WalletDatabase"] = database.ConnectionString,
            ["WalletServer:DatabaseProvider"] = provider.ToString(),
            ["Auth:Issuer"] = "test-wallet",
            ["Auth:Audience"] = "test-wallet",
            ["Auth:Secret"] = secret,
            ["Auth:AllowSystemApiKey"] = "true",
            ["WalletServer:SystemAuthorizationCode"] = SystemAuthorizationCode
        };

        await Program.Main(["setup", .. settings.Select(x => $"--{x.Key}={x.Value}")]);
        return new TestWalletServer(database, secret, settings, replaceServices);
    }

    public HttpClient CreateClient()
    {
        return _factory.CreateClient();
    }

    // a token for the system, as a service that holds the secret gets one
    public async Task<HttpClient> CreateSystemClient(CancellationToken cancellationToken)
    {
        var client = _factory.CreateClient();
        using var response = await client.PostAsync("/api/authorization/system/api-key",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("secret", _secret)]), cancellationToken);
        response.EnsureSuccessStatusCode();
        var apiKey = await response.Content.ReadFromJsonAsync<ApiKey>(cancellationToken)
                     ?? throw new InvalidOperationException("The server gave no api key.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.AccessToken.Value);
        return client;
    }

    // A token the server itself signed for someone who is not the system, carrying the system's code, as if it
    // had leaked.
    public async Task<HttpClient> CreateUserClient(string userId)
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, userId));
        claimsIdentity.AddClaim(new Claim(GrayMintClaimTypes.AuthCode, SystemAuthorizationCode));
        var authentication = _factory.Services.GetRequiredService<GrayMintAuthentication>();
        var apiKey = await authentication.CreateApiKey(claimsIdentity);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.AccessToken.Value);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}
