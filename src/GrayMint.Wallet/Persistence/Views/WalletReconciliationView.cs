using GrayMint.Wallet.Persistence.Models;
using GrayMint.Wallet.Reconciliation;

namespace GrayMint.Wallet.Persistence.Views;

// A wallet beside what its transfers add up to, and the balances its newest wallet transaction left, null for a
// wallet without one (WalletRepo.GetWalletDiscrepancies).
internal sealed class WalletReconciliationView
{
    public required WalletModel Wallet { get; init; }
    public required WalletTotals FromTransfers { get; init; }
    public required decimal? LastPostedBalance { get; init; }
    public required decimal? LastAvailableBalance { get; init; }
}
