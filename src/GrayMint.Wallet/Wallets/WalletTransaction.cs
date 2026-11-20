using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Wallets;

/// <summary>
/// A wallet's side of a transfer, a line of the wallet's statement: which way the amount went, and the balances the
/// transfer left on the wallet. Each transfer makes one on each of its two wallets, never changed or deleted.
/// </summary>
public class WalletTransaction
{
    /// <summary>The wallet's id.</summary>
    public required string WalletId { get; init; }

    /// <summary>
    /// The transfer's amount: below 0 on the wallet it comes from, above 0 on the one it goes to, and the other way
    /// round for a void, which undoes its pending transfer. What it did to the balances depends on the transfer's
    /// type: a pending transfer holds the money going out and only announces the money coming in, its post moves
    /// both, and its void gives the hold back.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>The wallet's posted balance after the transfer.</summary>
    public required decimal PostedBalance { get; init; }

    /// <summary>The wallet's available balance after the transfer.</summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>The transfer.</summary>
    public required WalletTransfer Transfer { get; init; }
}
