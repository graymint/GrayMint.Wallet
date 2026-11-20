using GrayMint.Common.Utils;

namespace GrayMint.Wallet.Wallets;

/// <summary>The fields of a wallet to change; one left out keeps its value.</summary>
public class WalletUpdateRequest
{
    /// <summary>
    /// The lowest the wallet's available balance may go: 0 for no credit, below 0 for a credit limit, null for no
    /// limit. A lower limit always takes; a higher one only while the wallet keeps to it. A root wallet takes none.
    /// </summary>
    public Patch<decimal?>? MinAvailableBalance { get; init; }
}
