using GrayMint.Wallet.Reconciliation;

namespace GrayMint.Wallet.Persistence.Views;

// A tenant's wallets in one currency, added up (WalletRepo.GetCurrencyTotals).
internal sealed class CurrencyTotalsView
{
    public required int TenantId { get; init; }
    public required string Currency { get; init; }
    public required int WalletCount { get; init; }
    public required WalletTotals Totals { get; init; }
}
