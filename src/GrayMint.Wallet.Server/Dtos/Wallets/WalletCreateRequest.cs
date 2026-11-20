namespace GrayMint.Wallet.Server.Dtos.Wallets;

/// <summary>A wallet to make. A tenant has one wallet for each owner, code and currency.</summary>
public class WalletCreateRequest
{
    /// <summary>Whose wallet it is, in the caller's own terms: 1 to 100 characters, with no space at either end.</summary>
    public required string OwnerReferenceKey { get; init; }

    /// <summary>
    /// Which of the owner's wallets it is, such as its credit or its commission: 1 to 50 characters, with no space at
    /// either end.
    /// </summary>
    public required string WalletCode { get; init; }

    /// <summary>The wallet's currency: 1 to 10 upper-case letters and digits, such as USD.</summary>
    public required string Currency { get; init; }

    // No default: a forgotten limit would be no limit, or none at all, and each is wrong for some wallet.
    /// <summary>
    /// The lowest the wallet's available balance may go: 0 for no credit, below 0 for a credit limit, null for no
    /// limit. A new wallet has nothing, so it is never above 0.
    /// </summary>
    public required decimal? MinAvailableBalance { get; init; }
}
