using GrayMint.Wallet.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet.Test.Helper;

// Starts the server's own program in the test's process, with the test's settings.
public sealed class TestWalletFactory(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
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
}
