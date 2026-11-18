using GrayMint.Wallet.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet.Test.Helper;

// Starts the server's own program in the test's process, with the test's settings.
public sealed class TestWalletFactory(
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? replaceServices) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // As host configuration these reach the program as its arguments, so it has them while it registers its
        // services. What is given to the web host builder arrives only after that.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));

        // not Development: a test never runs on the settings of the developer's own machine
        builder.UseEnvironment("Testing");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (replaceServices != null)
            builder.ConfigureTestServices(replaceServices);
    }
}
