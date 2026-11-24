using GrayMint.Common.AspNetCore;
using GrayMint.Common.OpenApi;
using GrayMint.Wallet.Server.ApiDocs;
using GrayMint.Wallet.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Server;

// The wallet as a service of its own, on PostgreSQL or SQL Server. Its one command:
//   setup   makes the database and the wallet's tables when they are not there, as a login that may make them
public class Program
{
    private const string SetupCommand = "setup";

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var connectionString = builder.Configuration.GetConnectionString("WalletDatabase")
                               ?? throw new InvalidOperationException("ConnectionStrings:WalletDatabase is missing.");

        const string providerSetting = $"{WalletServerOptions.SectionName}:{nameof(WalletServerOptions.DatabaseProvider)}";
        var databaseProvider = builder.Configuration.GetValue<WalletDatabaseProvider?>(providerSetting)
                               ?? throw new InvalidOperationException(
                                   $"{providerSetting} is missing: {string.Join(" or ", Enum.GetNames<WalletDatabaseProvider>())}.");

        builder.Services.AddGrayMintWalletDb(options => _ = databaseProvider switch {
            WalletDatabaseProvider.PostgreSql => options.UseNpgsql(connectionString),
            WalletDatabaseProvider.SqlServer => options.UseSqlServer(connectionString),
            _ => throw new NotSupportedException($"{providerSetting} {databaseProvider} is not known.")
        });

        // The setup runs as the tables' owner, whose settings need not hold the server's others: it returns before the
        // server's services are registered, and never starts the server, where their settings are checked.
        if (args.FirstOrDefault() == SetupCommand) {
            await using var setupApp = builder.Build();
            await setupApp.Services.UseGrayMintWallet(CancellationToken.None);
            return;
        }

        // No other site's page calls the server, so it lets none in; its API reference (/scalar) is a page of its own.
        builder.Services
            .AddGrayMintCommonServices(new RegisterServicesOptions { AddCors = false })
            .AddWalletApiDocs();

        builder.Services.Configure<MvcOptions>(options => options.Filters.Add<ServerFailureFilter>());

        // The server's one caller is the system, with one of the server's API keys: one key for the one caller, and a
        // second while a new one takes over. A key that is missing or weak stops the start.
        const string apiKeysSetting = $"{WalletServerOptions.SectionName}:{nameof(WalletServerOptions.ApiKeys)}";
        builder.Services
            .AddOptions<WalletServerOptions>()
            .BindConfiguration(WalletServerOptions.SectionName)
            .Validate(x => x.ApiKeys.Count > 0, $"{apiKeysSetting} is missing: the server needs a key.")
            .Validate(x => x.ApiKeys.All(WalletServerOptions.IsStrongApiKey),
                $"{apiKeysSetting} holds a weak key: each has at least {WalletServerOptions.MinApiKeyLength} " +
                "characters and no space at either end, such as 32 random bytes in base64.")
            .ValidateOnStart();

        builder.Services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName,
                null);

        builder.Services.AddAuthorization();
        builder.Services.AddGrayMintWallet();

        var webApp = builder.Build();
        webApp.UseGrayMintCommonServices(new UseServicesOptions { UseCors = false });
        webApp.UseGrayMintOpenApi(new UseOpenApiOptions { RedirectRootToUi = true });
        webApp.UseAuthentication();
        webApp.UseAuthorization();
        await webApp.RunAsync();
    }
}
