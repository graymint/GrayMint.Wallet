using EWallet.Persistence;
using EWallet.Server;
using EWallet.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

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
    public int SystemWalletId { get; set; }
    public string AuthSecret => "ezM4NDhDRjgzLTEyRjQtNDNFRC05NzNBLUE2M0VEODgzMzkyRn0=";
    public AuthorizationClient AuthorizationClient => new(HttpClient);
    public ApiKey SystemApiKey { get; private set; } = default!;

    private TestInit(Dictionary<string, string?> appSettings, string environment) {
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
    public static async Task<TestInit> Create(Dictionary<string, string?>? appSettings = null, string environment = "Development") {
        appSettings ??= new Dictionary<string, string?>();

        var testInit = new TestInit(appSettings, environment);
        await testInit.Init();
        return testInit;
    }

    private async Task Init() {
        // build appCreator
        SystemApiKey = await AuthorizationClient.CreateSystemApiKeyAsync(AuthSecret);
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(SystemApiKey.AccessToken.Scheme, SystemApiKey.AccessToken.Value);

        // create app
        var appsClient = new AppsClient(HttpClient);
        var app = await appsClient.CreateAsync();
        AppId = app.AppId;
        SystemWalletId = app.SystemWalletId;

        // attach its token
        var userApiKey = await AuthorizationClient.ResetUserApiKeyAsync(AppId.ToString());
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(userApiKey.AccessToken.Scheme, userApiKey.AccessToken.Value);
    }
}