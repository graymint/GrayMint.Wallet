namespace GrayMint.Wallet.Reconciliation;

/// <summary>
/// Where reconciliation found the books disagree. On reconciled books it lists nothing: each wallet's totals are what
/// its transfers add up to, its balances are the ones its newest wallet transaction left, and each tenant's wallets
/// in each currency add up to zero.
/// </summary>
public class WalletReconciliationReport
{
    /// <summary>How many wallets were checked: every one the caller can see.</summary>
    public required int WalletCount { get; init; }

    /// <summary>
    /// The wallets whose totals are not what their transfers add up to, or whose balances are not the ones their
    /// newest wallet transaction left, in the order of their ids.
    /// </summary>
    public required IReadOnlyList<WalletDiscrepancy> WalletDiscrepancies { get; init; }

    /// <summary>The currencies of a tenant whose wallets don't add up to zero, by tenant and currency.</summary>
    public required IReadOnlyList<CurrencyImbalance> CurrencyImbalances { get; init; }

    /// <summary>Whether nothing disagreed: no wallet discrepancy and no currency imbalance.</summary>
    public bool IsReconciled => WalletDiscrepancies.Count == 0 && CurrencyImbalances.Count == 0;
}
