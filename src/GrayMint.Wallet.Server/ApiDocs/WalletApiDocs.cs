using GrayMint.Common.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GrayMint.Wallet.Server.ApiDocs;

// The API's description: GrayMint's document, worded from the doc comments, with the server's title, version and
// introduction (ApiIntro.md), and its token named for what it is, one of the server's API keys.
internal static class WalletApiDocs
{
    public const string Title = "GrayMint Wallet";

    public static IServiceCollection AddWalletApiDocs(this IServiceCollection services)
    {
        var assembly = typeof(WalletApiDocs).Assembly;
        services.AddGrayMintOpenApi(new AddOpenApiOptions {
            Title = Title,
            Description = ReadIntro(),
            Version = assembly.GetName().Version?.ToString(3)
        });

        // after GrayMint's, which calls the bearer token a JWT
        services.Configure<OpenApiOptions>("v1", options => options.AddDocumentTransformer((document, _, _) => {
            if (document.Components?.SecuritySchemes?.TryGetValue("Bearer", out var scheme) == true &&
                scheme is OpenApiSecurityScheme bearer) {
                bearer.BearerFormat = null;
                bearer.Description = "One of the server's API keys (WalletServer:ApiKeys).";
            }

            return Task.CompletedTask;
        }));

        return services;
    }

    private static string ReadIntro()
    {
        var assembly = typeof(WalletApiDocs).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".ApiDocs.ApiIntro.md"));
        using var stream = assembly.GetManifestResourceStream(name) ??
                           throw new InvalidOperationException($"Could not read {name}.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
