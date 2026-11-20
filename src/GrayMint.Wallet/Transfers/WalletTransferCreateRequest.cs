namespace GrayMint.Wallet.Transfers;

/// <summary>A transfer to make.</summary>
public class WalletTransferCreateRequest
{
    /// <summary>
    /// The caller's key for the transfer, unique in the tenant: 1 to <see cref="WalletLimits.MaxExternalReferenceKeyLength"/>
    /// characters, with no space at either end. The same request sent again with it changes nothing, so a caller that
    /// can't tell whether its request went through sends it again.
    /// </summary>
    public required string ExternalReferenceKey { get; init; }

    /// <summary>The wallet the amount comes from.</summary>
    public required string FromWalletId { get; init; }

    /// <summary>The wallet the amount goes to: another wallet, of the same tenant and currency.</summary>
    public required string ToWalletId { get; init; }

    /// <summary>
    /// The amount: above 0, at most <see cref="WalletLimits.MaxAmount"/>, with at most
    /// <see cref="WalletLimits.AmountScale"/> decimals.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Whether the amount is held on the wallet it comes from, in place of moving at once, until the transfer is
    /// posted or voided.
    /// </summary>
    public bool IsPending { get; init; }

    /// <summary>
    /// What the transfer is for, in the host's own terms: 1 to <see cref="WalletLimits.MaxCodeLength"/> characters, with
    /// no space at either end, or null.
    /// </summary>
    public string? PurposeCode { get; init; }
}
