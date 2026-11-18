using System.Net;
using System.Net.Http.Json;
using GrayMint.Authorization.Abstractions;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Server.Accounts;
using GrayMint.Wallet.Server.Transfers;
using GrayMint.Wallet.Test.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class ServerTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task SetupMakesTheTablesWithTheirGuards(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        await using var dbContext = server.Database.CreateDbContext();
        await WalletSetup.ThrowIfIncomplete(dbContext, null, CancellationToken);
    }

    [TestMethod, EveryDatabase]
    public async Task SystemMovesMoneyOverHttpAndARetryChangesNothing(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        using var client = await server.CreateSystemClient(CancellationToken);

        var source = await CreateAccount(client, "source", null);
        var account = await CreateAccount(client, "owner", 0);
        var chain = new[] { Request(source, account, 25, key: "order-7/pay") };

        // the caller didn't hear back, so it sends the same request again
        var first = await Post<IReadOnlyList<Transfer>>(client, "/api/ledgers/1/transfers", chain);
        var again = await Post<IReadOnlyList<Transfer>>(client, "/api/ledgers/1/transfers", chain);
        Assert.AreEqual(first[0].TransferId, again[0].TransferId);

        var read = await client.GetFromJsonAsync<Account>($"/api/ledgers/1/accounts/{account.AccountId}", CancellationToken);
        Assert.AreEqual(25m, read?.Balance);
        var byKey = await client.GetFromJsonAsync<Transfer>("/api/ledgers/1/transfers/by-key?key=order-7%2Fpay", CancellationToken);
        Assert.AreEqual(first[0].TransferId, byKey?.TransferId);
        Assert.AreEqual(account.AccountId, byKey?.CreditAccountId);
    }

    // Every id is text on the wire, as the API's other users read it, and a transfer's type is its name.
    [TestMethod]
    public async Task IdsGoOutAsText()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = await server.CreateSystemClient(CancellationToken);
        var source = await CreateAccount(client, "source", null);
        var account = await CreateAccount(client, "owner", 0);
        await Post<IReadOnlyList<Transfer>>(client, "/api/ledgers/1/transfers",
            new[] { Request(source, account, 4, isPending: true, key: "held") });
        var settlements = await Post<IReadOnlyList<Transfer>>(client, "/api/ledgers/1/transfers/post", new[] { "held" });

        var accountJson = await client.GetStringAsync($"/api/ledgers/1/accounts/{account.AccountId}", CancellationToken);
        Assert.Contains($"\"accountId\":\"{account.AccountId}\"", accountJson);
        Assert.Contains("\"ledgerId\":\"1\"", accountJson);

        var transfersJson = await client.GetStringAsync(
            $"/api/ledgers/1/accounts/{account.AccountId}/transfers?count=10", CancellationToken);
        Assert.Contains($"\"transferId\":\"{settlements[0].TransferId}\"", transfersJson);
        Assert.Contains($"\"pendingTransferId\":\"{settlements[0].PendingTransferId}\"", transfersJson);
        Assert.Contains($"\"debitAccountId\":\"{source.AccountId}\"", transfersJson);
        Assert.Contains("\"type\":\"PostPending\"", transfersJson);

        // the page before the newest one goes on from its id
        var older = await client.GetFromJsonAsync<IReadOnlyList<Transfer>>(
            $"/api/ledgers/1/accounts/{account.AccountId}/transfers?count=10&beforeTransferId={settlements[0].TransferId}",
            CancellationToken);
        Assert.AreEqual(settlements[0].PendingTransferId, older?.Single().TransferId);
    }

    // An id that is no number names nothing, as one that is not there.
    [TestMethod]
    public async Task IdThatIsNoNumberIsNotFound()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = await server.CreateSystemClient(CancellationToken);
        var account = await CreateAccount(client, "owner", 0);

        foreach (var path in new[] {
                     "/api/ledgers/1/accounts/abc",
                     "/api/ledgers/1/accounts/-1",
                     "/api/ledgers/one/accounts/1",
                     $"/api/ledgers/1/accounts/{account.AccountId}/transfers?count=10&beforeTransferId=x"
                 }) {
            using var response = await client.GetAsync(path, CancellationToken);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
        }

        // null where a transfer belongs is a request wrong in itself
        using var nothing = await client.PostAsync("/api/ledgers/1/transfers",
            new StringContent("[null]", System.Text.Encoding.UTF8, "application/json"), CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, nothing.StatusCode);

        using var transfer = await client.PostAsJsonAsync("/api/ledgers/1/transfers", new[] {
            new TransferRequest { Key = "k", DebitAccountId = "abc", CreditAccountId = account.AccountId, Amount = 1 }
        }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, transfer.StatusCode);
    }

    [TestMethod, EveryDatabase]
    public async Task ServerAnswersRefusalsByTheirKind(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        using var client = await server.CreateSystemClient(CancellationToken);
        var account = await CreateAccount(client, "owner", 0);
        var other = await CreateAccount(client, "other", 0);

        using var overdraft = await client.PostAsJsonAsync("/api/ledgers/1/transfers",
            new[] { Request(account, other, 1) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, overdraft.StatusCode);

        using var otherLedger = await client.GetAsync($"/api/ledgers/2/accounts/{account.AccountId}", CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, otherLedger.StatusCode);

        using var invalid = await client.PostAsJsonAsync("/api/ledgers/1/transfers",
            new[] { Request(account, other, 0) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var twice = await client.PostAsJsonAsync("/api/ledgers/1/accounts", new AccountCreateRequest {
            OwnerId = "owner", Code = "credit", Currency = "USD", MinBalance = 0
        }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, twice.StatusCode);
    }

    [TestMethod]
    public async Task OnlyTheSystemGetsIn()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var anonymous = server.CreateClient();
        using var noToken = await anonymous.GetAsync("/api/ledgers/1/accounts?ownerId=owner", CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using var wrongSecret = await anonymous.PostAsync("/api/authorization/system/api-key",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("secret", Convert.ToBase64String(new byte[32]))]),
            CancellationToken);
        Assert.IsFalse(wrongSecret.IsSuccessStatusCode);

        anonymous.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-token");
        using var badToken = await anonymous.GetAsync("/api/ledgers/1/accounts?ownerId=owner", CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, badToken.StatusCode);
    }

    // The server hands its code to the system alone, so anyone else is not signed in, even with that code.
    [TestMethod]
    public async Task CallerWhoIsNotTheSystemIsNotSignedIn()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var user = await server.CreateUserClient("someone");
        using var read = await user.GetAsync("/api/ledgers/1/accounts?ownerId=owner", CancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    // With the first wall down, every address still asks for the system's permissions.
    [TestMethod]
    public async Task SignedInCallerWhoIsNotTheSystemMayDoNothing()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql, services =>
            services.AddScoped<IAuthorizationProvider, TestSignInEveryoneProvider>());
        using var system = await server.CreateSystemClient(CancellationToken);
        var account = await CreateAccount(system, "owner", null);
        var other = await CreateAccount(system, "other", 0);
        await Post<IReadOnlyList<Transfer>>(system, "/api/ledgers/1/transfers",
            new[] { Request(account, other, 1, isPending: true, key: "held") });

        using var user = await server.CreateUserClient("someone");
        var requests = new (HttpMethod Method, string Path, object? Body)[] {
            (HttpMethod.Get, $"/api/ledgers/1/accounts/{account.AccountId}", null),
            (HttpMethod.Get, "/api/ledgers/1/accounts?ownerId=owner", null),
            (HttpMethod.Get, $"/api/ledgers/1/accounts/{account.AccountId}/transfers?count=10", null),
            (HttpMethod.Get, "/api/ledgers/1/transfers/by-key?key=held", null),
            (HttpMethod.Post, "/api/ledgers/1/accounts", new AccountCreateRequest {
                OwnerId = "someone", Code = "credit", Currency = "USD", MinBalance = null
            }),
            (HttpMethod.Put, $"/api/ledgers/1/accounts/{other.AccountId}/min-balance", new { MinBalance = (decimal?)null }),
            (HttpMethod.Post, "/api/ledgers/1/transfers", new[] { Request(account, other, 1) }),
            (HttpMethod.Post, "/api/ledgers/1/transfers/post", new[] { "held" }),
            (HttpMethod.Post, "/api/ledgers/1/transfers/void", new[] { "held" })
        };
        foreach (var (method, path, body) in requests) {
            using var request = new HttpRequestMessage(method, path) {
                Content = body == null ? null : JsonContent.Create(body)
            };
            using var response = await user.SendAsync(request, CancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, $"{method} {path}");
        }

        Assert.AreEqual(1L, await server.Database.Count("SELECT count(*) FROM gmwallet.\"Transfers\"", CancellationToken));
        Assert.AreEqual(2L, await server.Database.Count("SELECT count(*) FROM gmwallet.\"Accounts\"", CancellationToken));
    }

    private static TransferRequest Request(Account debit, Account credit, decimal amount, bool isPending = false,
        string? key = null)
    {
        return new TransferRequest {
            Key = key ?? $"key-{Guid.CreateVersion7():N}",
            DebitAccountId = debit.AccountId,
            CreditAccountId = credit.AccountId,
            Amount = amount,
            IsPending = isPending
        };
    }

    private async Task<Account> CreateAccount(HttpClient client, string ownerId, decimal? minBalance)
    {
        return await Post<Account>(client, "/api/ledgers/1/accounts", new AccountCreateRequest {
            OwnerId = ownerId,
            Code = "credit",
            Currency = "USD",
            MinBalance = minBalance
        });
    }

    private async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
        return await response.Content.ReadFromJsonAsync<T>(CancellationToken)
               ?? throw new InvalidOperationException($"{path} gave nothing.");
    }
}
