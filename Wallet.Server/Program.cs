using EWallet.Models;
using EWallet.Persistence;
using EWallet.Repo;
using EWallet.Server.Security;
using EWallet.Service;
using GrayMint.Authorization.MicroserviceAuthorization;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.EntityFrameworkCore;
using GrayMint.Common.Swagger;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddGrayMintCommonServices(new RegisterServicesOptions());
        builder.Services.AddDbContext<WalletDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("WalletDatabase")));
        builder.Services.AddScoped<AppService>();
        builder.Services.AddScoped<WalletService>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddScoped<WalletRepo>();
        builder.Services.AddHttpClient();
        builder.Services.AddGrayMintSwagger(title: "wallet", true);

        // GrayMint Authentication
        builder.AddGrayMintCommonAuthorizationForMicroservice<AuthorizationProvider>();

        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
        var webApp = builder.Build();
        webApp.UseGrayMintCommonServices(new UseServicesOptions());
        webApp.UseGrayMintSwagger();
        await webApp.Services.UseGrayMintDatabaseCommand<WalletDbContext>(args);

        // Initializing App
        using (var scope = webApp.Services.CreateScope())
        {
            var appDbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
            await EfCoreUtil.UpdateEnums<TransactionTypeLookup, TransactionType>(appDbContext.TransactionTypes);
            await appDbContext.SaveChangesAsync();
            await webApp.UseGrayMinCommonAuthorizationForMicroservice();
        }

        await GrayMintApp.RunAsync(webApp, args);
    }
}