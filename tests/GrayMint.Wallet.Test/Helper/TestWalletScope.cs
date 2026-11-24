using GrayMint.Wallet.Wallets;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Helper;

// The wallet's services in one scope, as a request of a host gets them: one context for all three.
public sealed class TestWalletScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public WalletService Wallets { get; } = scope.ServiceProvider.GetRequiredService<WalletService>();
    public WalletTransferService Transfers { get; } = scope.ServiceProvider.GetRequiredService<WalletTransferService>();

    public WalletReconciliationService Reconciliation { get; } =
        scope.ServiceProvider.GetRequiredService<WalletReconciliationService>();

    public ValueTask DisposeAsync()
    {
        return scope.DisposeAsync();
    }
}
