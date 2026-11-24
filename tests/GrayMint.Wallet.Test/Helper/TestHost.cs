using GrayMint.Wallet.Server;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Helper;

// A host that keeps the wallet's tables in its own database and moves money in its own transactions: its database
// holds its own table, and the wallet's setup runs on the host's connection, as a host's setup runs it.
public sealed class TestHost : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public TestWallet Wallet { get; }

    // the connections the host's context opens, and those the wallet's would open by itself
    public TestConnectionCounter HostConnections { get; } = new();
    public TestConnectionCounter WalletConnections { get; } = new();

    private TestHost(TestWallet testWallet)
    {
        Wallet = testWallet;
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => testWallet.UseDatabase(options).AddInterceptors(HostConnections));
        services.AddGrayMintWallet();
        services.AddGrayMintWalletDb<TestDbContext>((connection, options) =>
            testWallet.UseDatabase(options, connection).AddInterceptors(WalletConnections));

        _serviceProvider = services.BuildServiceProvider();
    }

    public static async Task<TestHost> Create(WalletDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var host = new TestHost(TestWallet.CreateEmpty(provider));
        try {
            await using var scope = host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync(cancellationToken);
            await host._serviceProvider.UseGrayMintWallet(cancellationToken);
            return host;
        }
        catch {
            // no test gets it to drop, so its database goes here
            await host.DisposeAsync();
            throw;
        }
    }

    public AsyncServiceScope CreateScope()
    {
        return _serviceProvider.CreateAsyncScope();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await Wallet.DisposeAsync();
    }
}
