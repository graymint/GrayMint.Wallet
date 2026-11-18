using GrayMint.Authorization.MicroserviceAuthorization;
using GrayMint.Common.AspNetCore;
using GrayMint.Wallet.Server.Security;
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
        var providerSetting = $"{WalletServerOptions.SectionName}:{nameof(WalletServerOptions.DatabaseProvider)}";
        var databaseProvider = builder.Configuration.GetValue<WalletDatabaseProvider?>(providerSetting)
                               ?? throw new InvalidOperationException(
                                   $"{providerSetting} is missing: {string.Join(" or ", Enum.GetNames<WalletDatabaseProvider>())}.");

        // no browser calls the server, so it lets no other site's page in
        builder.Services.AddGrayMintCommonServices(new RegisterServicesOptions { AddCors = false });
        builder.Services
            .AddOptions<WalletServerOptions>()
            .BindConfiguration(WalletServerOptions.SectionName)
            .Validate(x => !string.IsNullOrWhiteSpace(x.SystemAuthorizationCode),
                $"{WalletServerOptions.SectionName}:{nameof(WalletServerOptions.SystemAuthorizationCode)} is missing.")
            .ValidateOnStart();
        builder.AddGrayMintCommonAuthorizationForMicroservice<WalletAuthorizationProvider>();
        builder.Services.AddGrayMintWallet();
        builder.Services.AddGrayMintWalletDb(options => _ = databaseProvider switch {
            WalletDatabaseProvider.PostgreSql => options.UseNpgsql(connectionString),
            WalletDatabaseProvider.SqlServer => options.UseSqlServer(connectionString),
            _ => throw new NotSupportedException($"{providerSetting} {databaseProvider} is not known.")
        });

        var webApp = builder.Build();
        if (args.FirstOrDefault() == SetupCommand) {
            await webApp.Services.UseGrayMintWallet(CancellationToken.None);
            return;
        }

        webApp.UseGrayMintCommonServices(new UseServicesOptions { UseCors = false });
        await webApp.UseGrayMinCommonAuthorizationForMicroservice();
        await webApp.RunAsync();
    }
}
