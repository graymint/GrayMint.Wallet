using GrayMint.Wallet.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.Wallet;

internal sealed class WalletStartup(IServiceScopeFactory serviceScopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await WalletSetup.ThrowIfIncomplete(
            scope.ServiceProvider.GetRequiredService<WalletDbContext>(),
            scope.ServiceProvider.GetService<HostDatabase>(),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
