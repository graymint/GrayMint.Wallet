using System.Formats.Asn1;
using EWallet.Models;
using EWallet.Persistence;
using EWallet.Server;
using EWallet.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EWallet.Test.Helper;
public class TestInit
{
    public IServiceScope Scope { get; }
    public WalletDbContext WalletDbContext => Scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    public WebApplicationFactory<Program> WebApp { get; }
    public HttpClient HttpClient { get; set; }
    public AppsClient AppsClient => new(HttpClient);
    public CurrenciesClient CurrenciesClient => new(HttpClient);
    public WalletsClient WalletsClient => new(HttpClient);
    public OrdersClient OrdersClient => new(HttpClient);
    public int AppId { get; set; }
    private TestInit(Dictionary<string, string?> appSettings, string environment)
    {
        // Application
        WebApp = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);

                builder.UseEnvironment(environment);
            });

        // Client
        HttpClient = WebApp.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        Scope = WebApp.Services.CreateScope();
    }
    public static async Task<TestInit> Create(Dictionary<string, string?>? appSettings = null, string environment = "Development")
    {
        appSettings ??= new Dictionary<string, string?>();

        var testInit = new TestInit(appSettings, environment);
        await testInit.Init();
        return testInit;
    }

    private async Task Init()
    {
        // Create new app
        AppId = await AppsClient.CreateAsync();
    }
}