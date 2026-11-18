namespace GrayMint.Wallet.Reconciliation;

// An account whose stored totals are not what its transfers add up to.
public class WalletTotalsMismatch
{
    public required long AccountId { get; init; }
    public required int LedgerId { get; init; }
    public required WalletAccountTotals Stored { get; init; }
    public required WalletAccountTotals FromTransfers { get; init; }
}
