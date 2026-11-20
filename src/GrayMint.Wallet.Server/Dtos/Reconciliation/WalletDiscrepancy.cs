namespace GrayMint.Wallet.Server.Dtos.Reconciliation;

// inside the namespace: from outside, Wallet would name the namespace GrayMint.Wallet
using GrayMint.Wallet.Server.Dtos.Wallets;

/// <summary>
/// A wallet whose totals are not what its transfers add up to, or whose balances are not the ones its newest wallet
/// transaction left: a write that went round the wallet, or a fault in it.
/// </summary>
public class WalletDiscrepancy
{
    /// <summary>The wallet, with the totals and balances it holds.</summary>
    public required Wallet Wallet { get; init; }

    /// <summary>What the wallet's transfers add up to, which its totals should be.</summary>
    public required WalletTotals FromTransfers { get; init; }

    /// <summary>
    /// The wallet's newest wallet transaction, whose balances the wallet should hold; null when it has none, when its
    /// balances should be 0.
    /// </summary>
    public required WalletTransaction? LastTransaction { get; init; }
}
