using System.Net.Http.Headers;
using System.Security.Cryptography;
using GrayMint.Wallet.Server;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GrayMint.Wallet.Test.Helper;

// The server as a deployment runs it: its setup command makes the database, then it serves, with settings of its
// own and none of Development's, and two API keys, as while a new key takes over from the old one.
public sealed class TestWalletServer : IAsyncDisposable
{
    private readonly TestWalletFactory _factory;

    public TestWallet Database { get; }
    public IReadOnlyList<string> ApiKeys { get; }

    private TestWalletServer(TestWallet database, IReadOnlyList<string> apiKeys,
        IReadOnlyDictionary<string, string?> settings)
    {
        Database = database;
        ApiKeys = apiKeys;
        _factory = new TestWalletFactory(settings);
    }

    public static async Task<TestWalletServer> Create(WalletDatabaseProvider provider,
        IReadOnlyList<string>? apiKeys = null, CancellationToken cancellationToken = default)
    {
        var database = TestWallet.CreateEmpty(provider, cancellationToken);
        var keys = apiKeys ?? [CreateApiKey(), CreateApiKey()];
        var settings = new Dictionary<string, string?> {
            ["ConnectionStrings:WalletDatabase"] = database.ConnectionString,
            ["WalletServer:DatabaseProvider"] = provider.ToString()
        };

        for (var i = 0; i < keys.Count; i++)
            settings[$"WalletServer:ApiKeys:{i}"] = keys[i];

        try {
            var setupArgs = settings.Select(x => $"--{x.Key}={x.Value}").Prepend("setup").ToArray();
            await Program.Main(setupArgs);
            return new TestWalletServer(database, keys, settings);
        }
        catch {
            // no test gets it to drop, so its database goes here
            await database.DisposeAsync();
            throw;
        }
    }

    // 32 random bytes in base64, as docs/deployment.md makes a key
    public static string CreateApiKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public HttpClient CreateClient()
    {
        return _factory.CreateClient();
    }

    // a client that reports a redirect, where the default one follows it
    public HttpClient CreateClientWithoutRedirects()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // the system, with one of the server's keys
    public HttpClient CreateSystemClient(string? apiKey = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey ?? ApiKeys[0]);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}
