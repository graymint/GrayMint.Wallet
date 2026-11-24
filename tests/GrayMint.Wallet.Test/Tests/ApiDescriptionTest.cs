using System.Net;
using System.Text.Json.Nodes;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test.Tests;

// The API's description, the one /scalar shows, says something about every operation, parameter, section and field:
// what a caller reads there comes from the doc comments, and an undescribed name is a guess.
[TestClass]
public class ApiDescriptionTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod]
    public async Task EveryNameIsDescribed()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var client = server.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json", CancellationToken);
        var document = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("No description.");
        var schemas = document["components"]?["schemas"]?.AsObject() ?? [];
        var missing = new List<string>();

        Assert.AreEqual("GrayMint Wallet", (string?)document["info"]?["title"]);
        if (IsEmpty(document["info"]?["description"]))
            missing.Add("the introduction");

        var ownSchemas = new HashSet<string>();
        var ownTags = new HashSet<string>();
        foreach (var (path, pathItem) in document["paths"]?.AsObject() ?? []) {
            foreach (var (method, operationNode) in pathItem?.AsObject() ?? []) {
                var operation = operationNode?.AsObject() ??
                                throw new InvalidOperationException($"{path} has no operation.");

                ownTags.UnionWith(operation["tags"]?.AsArray().Select(x => (string?)x).OfType<string>() ?? []);
                var name = $"{method.ToUpperInvariant()} {path}";
                if (IsEmpty(operation["summary"]))
                    missing.Add($"{name}: its summary");

                missing.AddRange((operation["parameters"]?.AsArray() ?? [])
                    .Where(parameter => IsEmpty(parameter?["description"]))
                    .Select(parameter => $"{name}: parameter {parameter?["name"]}"));

                if (operation["requestBody"] is { } requestBody && IsEmpty(requestBody["description"]))
                    missing.Add($"{name}: its body");

                CollectReferences(operation, schemas, ownSchemas);
            }
        }

        missing.AddRange((document["tags"]?.AsArray() ?? [])
            .Where(tag => ownTags.Contains((string?)tag?["name"] ?? "") && IsEmpty(tag?["description"]))
            .Select(tag => $"section {tag?["name"]}"));

        foreach (var name in ownSchemas.Order()) {
            var schema = schemas[name]?.AsObject();
            if (schema == null || schema["properties"] == null && schema["enum"] == null)
                continue; // a plain value

            if (IsEmpty(schema["description"]))
                missing.Add($"type {name}");

            foreach (var (propertyName, property) in schema["properties"]?.AsObject() ?? [])
                if (IsEmpty(property?["description"]))
                    missing.Add($"type {name}: {propertyName}");
        }

        Assert.IsEmpty(missing, $"{missing.Count} names have no description:\n{string.Join("\n", missing)}");
    }

    // the reference is where a reader lands, with no token
    [TestMethod]
    public async Task RootLeadsToTheReference()
    {
        await using var server = await TestWalletServer.Create(WalletDatabaseProvider.PostgreSql);
        using var withoutRedirects = server.CreateClientWithoutRedirects();
        using var root = await withoutRedirects.GetAsync("/", CancellationToken);
        Assert.AreEqual(HttpStatusCode.Redirect, root.StatusCode);
        Assert.AreEqual("/scalar", root.Headers.Location?.OriginalString);

        // Scalar's own address leads on to its page
        using var client = server.CreateClient();
        using var reference = await client.GetAsync("/scalar", CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reference.StatusCode);
        Assert.AreEqual("text/html", reference.Content.Headers.ContentType?.MediaType);
    }

    private static bool IsEmpty(JsonNode? node)
    {
        return string.IsNullOrWhiteSpace((string?)node);
    }

    // the schemas a node names, and the ones they name in turn
    private static void CollectReferences(JsonNode? node, JsonObject schemas, HashSet<string> names)
    {
        switch (node) {
            case JsonObject jsonObject:
                foreach (var (key, value) in jsonObject) {
                    if (key == "$ref" && ((string?)value)?.Split('/')[^1] is { } name) {
                        if (names.Add(name))
                            CollectReferences(schemas[name], schemas, names);
                        continue;
                    }

                    CollectReferences(value, schemas, names);
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                    CollectReferences(item, schemas, names);
                break;
        }
    }
}
