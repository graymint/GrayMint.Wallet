using GrayMint.Wallet.Wallets;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet.Test.Helper;

// The wallet's services in one scope, as a request of a host gets them: one context for all three.
public sealed class TestWalletScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;

    public WalletService Wallets { get; }
    public WalletTransferService Transfers { get; }
    public WalletReconciliationService Reconciliation { get; }

    public TestWalletScope(AsyncServiceScope scope)
    {
        _scope = scope;
        Wallets = scope.ServiceProvider.GetRequiredService<WalletService>();
        Transfers = scope.ServiceProvider.GetRequiredService<WalletTransferService>();
        Reconciliation = scope.ServiceProvider.GetRequiredService<WalletReconciliationService>();
    }

    public ValueTask DisposeAsync()
    {
        return _scope.DisposeAsync();
    }
}
