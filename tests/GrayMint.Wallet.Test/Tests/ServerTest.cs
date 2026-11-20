using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrayMint.Common.Utils;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Server.Dtos.Reconciliation;
using GrayMint.Wallet.Server.Dtos.Wallets;
using GrayMint.Wallet.Server.Dtos.Transfers;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;
using ServerWallet = GrayMint.Wallet.Server.Dtos.Wallets.Wallet;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class ServerTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    // The setup runs as the tables' owner, whose settings need not hold the server's others: it takes the database's
    // alone, as docs/deployment.md shows it.
    [TestMethod, EveryDatabase]
    public async Task SetupNeedsOnlyTheDatabasesSettings(WalletDatabaseProvider provider)
    {
        await using var database = TestWallet.CreateEmpty(provider);
        await Program.Main(["setup", $"--ConnectionStrings:WalletDatabase={database.ConnectionString}",
            $"--WalletServer:DatabaseProvider={provider}"]);

        await using var dbContext = database.CreateDbContext();
        await WalletSetup.ThrowIfNotReady(dbContext, null, CancellationToken);
    }

    [TestMethod, EveryDatabase]
    public async Task SystemMovesMoneyOverHttpAndARetryChangesNothing(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        using var client = server.CreateSystemClient();

        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        var linked = new[] { Request(source, wallet, 25, externalReferenceKey: "order-7/pay") };

        // the caller didn't hear back, so it sends the same request again
        var first = await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers", linked);
        var again = await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers", linked);
        Assert.AreEqual(first[0].TransferId, again[0].TransferId);

        var read = await client.GetFromJsonAsync<ServerWallet>($"/api/tenants/1/wallets/{wallet.WalletId}", CancellationToken);
        Assert.AreEqual(25m, read?.PostedBalance);
        var byKey = await client.GetFromJsonAsync<Transfer>("/api/tenants/1/transfers?externalReferenceKey=order-7%2Fpay", CancellationToken);
        Assert.AreEqual(first[0].TransferId, byKey?.TransferId);
        Assert.AreEqual(wallet.WalletId, byKey?.ToWalletId);
    }

    // Every id is text on the wire, as the API's other users read it, and a transfer's type is its name.
    [TestMethod]
    public async Task IdsGoOutAsText()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers",
            new[] { Request(source, wallet, 4, isPending: true, externalReferenceKey: "held") });
        var settlements = await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers/post", new[] { "held" });

        var walletJson = await client.GetStringAsync($"/api/tenants/1/wallets/{wallet.WalletId}", CancellationToken);
        Assert.Contains($"\"walletId\":\"{wallet.WalletId}\"", walletJson);
        Assert.Contains("\"tenantId\":\"1\"", walletJson);

        var transactionsJson = await client.GetStringAsync(
            $"/api/tenants/1/wallets/{wallet.WalletId}/transactions?limit=10", CancellationToken);
        Assert.Contains($"\"walletId\":\"{wallet.WalletId}\"", transactionsJson);
        Assert.Contains($"\"transferId\":\"{settlements[0].TransferId}\"", transactionsJson);
        Assert.Contains($"\"pendingTransferId\":\"{settlements[0].PendingTransferId}\"", transactionsJson);
        Assert.Contains($"\"fromWalletId\":\"{source.WalletId}\"", transactionsJson);
        Assert.Contains("\"type\":\"PostPending\"", transactionsJson);

        // the page before the newest one goes on from its transfer's id
        var older = await client.GetFromJsonAsync<IReadOnlyList<WalletTransaction>>(
            $"/api/tenants/1/wallets/{wallet.WalletId}/transactions?limit=10&beforeTransferId={settlements[0].TransferId}",
            CancellationToken);
        Assert.AreEqual(settlements[0].PendingTransferId, older?.Single().Transfer.TransferId);
    }

    // A wallet's statement over HTTP: a hold keeps the posted balance and holds back what the wallet may spend.
    [TestMethod]
    public async Task SystemReadsAWalletsTransactionsOverHttp()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers",
            new[] { Request(source, wallet, 4, isPending: true, externalReferenceKey: "held") });
        await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers/post", new[] { "held" });

        var transactions = await client.GetFromJsonAsync<IReadOnlyList<WalletTransaction>>(
                               $"/api/tenants/1/wallets/{source.WalletId}/transactions?limit=10", CancellationToken)
                           ?? throw new InvalidOperationException("No transactions.");
        CollectionAssert.AreEqual(new[] { TransferType.PostPending, TransferType.Pending },
            transactions.Select(x => x.Transfer.Type).ToArray());
        CollectionAssert.AreEqual(new[] { -4m, -4m }, transactions.Select(x => x.Amount).ToArray());
        CollectionAssert.AreEqual(new[] { -4m, 0m }, transactions.Select(x => x.PostedBalance).ToArray());
        CollectionAssert.AreEqual(new[] { -4m, -4m }, transactions.Select(x => x.AvailableBalance).ToArray());
    }

    // An id that is no number names nothing, as one that is not there.
    [TestMethod]
    public async Task IdThatIsNoNumberIsNotFound()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var wallet = await CreateWallet(client, "owner", 0);

        foreach (var path in new[] {
                     "/api/tenants/1/wallets/abc",
                     "/api/tenants/1/wallets/-1",
                     "/api/tenants/one/wallets/1",
                     $"/api/tenants/1/wallets/{wallet.WalletId}/transactions?limit=10&beforeTransferId=x"
                 }) {
            using var response = await client.GetAsync(path, CancellationToken);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
        }

        // null where a transfer belongs is a request wrong in itself, answered 400 with what is missing; a null
        // dereference would answer 500, as a failure of the server's own.
        using var nothing = await client.PostAsync("/api/tenants/1/transfers",
            new StringContent("[null]", System.Text.Encoding.UTF8, "application/json"), CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Contains("One of the linked transfers is missing",
            await nothing.Content.ReadAsStringAsync(CancellationToken));

        using var transfer = await client.PostAsJsonAsync("/api/tenants/1/transfers", new[] {
            new TransferCreateRequest { ExternalReferenceKey = "k", FromWalletId = "abc", ToWalletId = wallet.WalletId, Amount = 1 }
        }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, transfer.StatusCode);
    }

    [TestMethod, EveryDatabase]
    public async Task ServerAnswersRefusalsByTheirKind(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        using var client = server.CreateSystemClient();
        var wallet = await CreateWallet(client, "owner", 0);
        var other = await CreateWallet(client, "other", 0);

        using var overdraft = await client.PostAsJsonAsync("/api/tenants/1/transfers",
            new[] { Request(wallet, other, 1) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, overdraft.StatusCode);

        using var otherTenant = await client.GetAsync($"/api/tenants/2/wallets/{wallet.WalletId}", CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, otherTenant.StatusCode);

        using var invalid = await client.PostAsJsonAsync("/api/tenants/1/transfers",
            new[] { Request(wallet, other, 0) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var twice = await client.PostAsJsonAsync("/api/tenants/1/wallets", new WalletCreateRequest {
            OwnerReferenceKey = "owner", WalletCode = "credit", Currency = "USD", MinAvailableBalance = 0
        }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, twice.StatusCode);
    }

    // A failure of the server's own, here a table gone from under it, answers 500, so the caller sends the request
    // again: 400 would tell it that the request is wrong in itself.
    [TestMethod, EveryDatabase]
    public async Task ServerFailureAnswers500(WalletDatabaseProvider provider)
    {
        await using var server = await TestWalletServer.Create(provider);
        using var client = server.CreateSystemClient();
        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        await server.Database.Execute("DROP TABLE gmwallet.\"WalletTransactions\"", CancellationToken);

        using var response = await client.PostAsJsonAsync("/api/tenants/1/transfers",
            new[] { Request(source, wallet, 1) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // Every address asks for one of the server's keys. The addresses are read from the API's description, so a new one
    // is held to it too: no key, another key, the server's key with a character more or less, or without its scheme,
    // answers 401.
    [TestMethod]
    public async Task EveryAddressAsksForTheServersKey()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var anonymous = server.CreateClient();
        var description = JsonNode.Parse(await anonymous.GetStringAsync("/openapi/v1.json", CancellationToken))
                          ?.AsObject() ?? throw new InvalidOperationException("No description.");
        var operations = (description["paths"]?.AsObject() ?? [])
            .SelectMany(path => (path.Value?.AsObject() ?? []).Select(method => (
                Method: new HttpMethod(method.Key.ToUpperInvariant()),
                Address: Regex.Replace(path.Key, "{[^}]+}", "1"))))
            .ToArray();
        Assert.IsTrue(operations.Length >= 10, $"Only {operations.Length} addresses are described.");

        var key = server.ApiKeys[0];
        string?[] refusedAuthorizations =
            [null, $"Bearer {TestWalletServer.CreateApiKey()}", $"Bearer {key}x", $"Bearer {key[..^1]}", key];
        foreach (var (method, address) in operations)
        foreach (var authorization in refusedAuthorizations) {
            using var request = new HttpRequestMessage(method, address) {
                Content = method == HttpMethod.Get ? null : new StringContent("{}", Encoding.UTF8, "application/json")
            };
            if (authorization != null)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var response = await anonymous.SendAsync(request, CancellationToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode,
                $"{method} {address} with {authorization ?? "no key"}");
        }

        Assert.AreEqual(0L, await server.Database.Count("SELECT count(*) FROM gmwallet.\"Wallets\"", CancellationToken));
    }

    // Either of the server's keys gets in, so a new key can take over before the old one goes.
    [TestMethod]
    public async Task EitherOfTheServersKeysGetsIn()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        foreach (var apiKey in server.ApiKeys) {
            using var client = server.CreateSystemClient(apiKey);
            using var response = await client.GetAsync("/api/tenants/1/wallets?ownerReferenceKey=owner", CancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // A server without a key, or with a weak one, doesn't start: a short key, or one a person made up, is guessed.
    [TestMethod]
    public async Task ServerWithoutAStrongKeyDoesNotStart()
    {
        string[][] weakKeys = [[], ["a-key-a-person-made-up"], [$" {TestWalletServer.CreateApiKey()}"]];
        foreach (var apiKeys in weakKeys) {
            await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql, apiKeys);
            var exception = Assert.Throws<Exception>(() => server.CreateClient());
            Assert.Contains("WalletServer:ApiKeys", exception.ToString());
        }
    }

    // A server without its database's settings stops before it does anything, and says which one is missing. Not in
    // Development, whose settings would fill in what is missing.
    [TestMethod]
    public async Task ServerWithoutItsDatabaseSettingsDoesNotStart()
    {
        var noConnection = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Program.Main(
            ["setup", "--environment=Testing", "--WalletServer:DatabaseProvider=PostgreSql"]));
        Assert.Contains("ConnectionStrings:WalletDatabase", noConnection.Message);

        var noProvider = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Program.Main(
            ["setup", "--environment=Testing", "--ConnectionStrings:WalletDatabase=Host=127.0.0.1"]));
        Assert.Contains("WalletServer:DatabaseProvider", noProvider.Message);
    }

    // A void gives the held amount back, and a limit is set, but not on a root wallet, which has none.
    [TestMethod]
    public async Task SystemVoidsAHoldAndSetsALimitOverHttp()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        var held = await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers",
            new[] { Request(source, wallet, 4, isPending: true, externalReferenceKey: "held") });

        var voided = await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers/void", new[] { "held" });
        Assert.AreEqual(TransferType.VoidPending, voided.Single().Type);
        Assert.AreEqual(held[0].TransferId, voided[0].PendingTransferId);
        var afterVoid = await client.GetFromJsonAsync<ServerWallet>($"/api/tenants/1/wallets/{wallet.WalletId}",
            CancellationToken);
        Assert.AreEqual(0m, afterVoid?.PendingIn);
        Assert.AreEqual(0m, afterVoid?.PostedBalance);

        using var limit = await client.PatchAsJsonAsync($"/api/tenants/1/wallets/{wallet.WalletId}",
            new WalletUpdateRequest { MinAvailableBalance = new Patch<decimal?>(-50) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, limit.StatusCode);
        Assert.AreEqual(-50m, (await limit.Content.ReadFromJsonAsync<ServerWallet>(CancellationToken))?.MinAvailableBalance);

        var root = await client.GetFromJsonAsync<ServerWallet>("/api/tenants/1/wallets/root?currency=USD",
            CancellationToken) ?? throw new InvalidOperationException("No root wallet.");
        using var rootLimit = await client.PatchAsJsonAsync($"/api/tenants/1/wallets/{root.WalletId}",
            new WalletUpdateRequest { MinAvailableBalance = new Patch<decimal?>(0) }, CancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, rootLimit.StatusCode);
    }

    // The root wallet, made at the first call, tops a wallet up and goes below zero by as much.
    [TestMethod]
    public async Task RootWalletTopsAWalletUp()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var root = await client.GetFromJsonAsync<ServerWallet>("/api/tenants/1/wallets/root?currency=USD",
            CancellationToken) ?? throw new InvalidOperationException("No root wallet.");
        var wallet = await CreateWallet(client, "owner", 0);
        await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers", new[] { Request(root, wallet, 10) });

        var rootAfter = await client.GetFromJsonAsync<ServerWallet>("/api/tenants/1/wallets/root?currency=USD",
            CancellationToken);
        Assert.AreEqual(root.WalletId, rootAfter?.WalletId);
        Assert.AreEqual(-10m, rootAfter?.PostedBalance);
        Assert.IsNull(rootAfter?.MinAvailableBalance);
    }

    // Nothing on reconciled books; then a wallet whose total was changed behind the server, and its tenant's currency,
    // which no longer adds up to zero.
    [TestMethod]
    public async Task SystemReconcilesTheBooksOverHttp()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateSystemClient();
        var source = await CreateWallet(client, "source", null);
        var wallet = await CreateWallet(client, "owner", 0);
        await Post<IReadOnlyList<Transfer>>(client, "/api/tenants/1/transfers", new[] { Request(source, wallet, 10) });

        var reconciled = await client.GetFromJsonAsync<ReconciliationReport>("/api/reconciliation", CancellationToken)
                         ?? throw new InvalidOperationException("No report.");
        Assert.IsTrue(reconciled.IsReconciled);
        Assert.AreEqual(2, reconciled.WalletCount);
        Assert.IsEmpty(reconciled.WalletDiscrepancies);
        Assert.IsEmpty(reconciled.CurrencyImbalances);

        await server.Database.Execute(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedIn\" = 11 WHERE \"WalletId\" = {wallet.WalletId}",
            CancellationToken);
        var report = await client.GetFromJsonAsync<ReconciliationReport>("/api/reconciliation", CancellationToken)
                     ?? throw new InvalidOperationException("No report.");
        Assert.IsFalse(report.IsReconciled);
        var discrepancy = report.WalletDiscrepancies.Single();
        Assert.AreEqual(wallet.WalletId, discrepancy.Wallet.WalletId);
        Assert.AreEqual(11m, discrepancy.Wallet.PostedIn);
        Assert.AreEqual(10m, discrepancy.FromTransfers.PostedIn);
        Assert.AreEqual(10m, discrepancy.LastTransaction?.PostedBalance);
        var imbalance = report.CurrencyImbalances.Single();
        Assert.AreEqual("1", imbalance.TenantId);
        Assert.AreEqual("USD", imbalance.Currency);
        Assert.AreEqual(11m, imbalance.Totals.PostedIn);
        Assert.AreEqual(10m, imbalance.Totals.PostedOut);
    }

    private static TransferCreateRequest Request(ServerWallet fromWallet, ServerWallet toWallet, decimal amount,
        bool isPending = false, string? externalReferenceKey = null)
    {
        return new TransferCreateRequest {
            ExternalReferenceKey = externalReferenceKey ?? $"key-{Guid.CreateVersion7():N}",
            FromWalletId = fromWallet.WalletId,
            ToWalletId = toWallet.WalletId,
            Amount = amount,
            IsPending = isPending
        };
    }

    private async Task<ServerWallet> CreateWallet(HttpClient client, string ownerReferenceKey, decimal? minAvailableBalance)
    {
        return await Post<ServerWallet>(client, "/api/tenants/1/wallets", new WalletCreateRequest {
            OwnerReferenceKey = ownerReferenceKey,
            WalletCode = "credit",
            Currency = "USD",
            MinAvailableBalance = minAvailableBalance
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
