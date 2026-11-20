namespace GrayMint.Wallet.Server.Dtos.Wallets;

/// <summary>
/// A wallet of a tenant, as it was when it was read. It keeps the totals of its transfers, posted and pending, of the
/// money in and of the money out.
/// </summary>
public class Wallet
{
    /// <summary>The wallet's id.</summary>
    public required string WalletId { get; init; }

    /// <summary>The tenant the wallet is in.</summary>
    public required string TenantId { get; init; }

    /// <summary>Whose wallet it is, in the caller's own terms.</summary>
    public required string OwnerReferenceKey { get; init; }

    /// <summary>Which of the owner's wallets it is, such as its credit or its commission.</summary>
    public required string WalletCode { get; init; }

    /// <summary>The wallet's currency, such as USD.</summary>
    public required string Currency { get; init; }

    /// <summary>
    /// The lowest <c>availableBalance</c> may go: 0 for no credit, below 0 for a credit limit, null for no limit.
    /// </summary>
    public required decimal? MinAvailableBalance { get; init; }

    /// <summary>The money posted in.</summary>
    public required decimal PostedIn { get; init; }

    /// <summary>The money posted out.</summary>
    public required decimal PostedOut { get; init; }

    /// <summary>The money pending in, which can't be spent before it is posted.</summary>
    public required decimal PendingIn { get; init; }

    /// <summary>The money pending out, held until it is posted or voided.</summary>
    public required decimal PendingOut { get; init; }

    /// <summary>The money posted in minus the money posted out.</summary>
    public required decimal PostedBalance { get; init; }

    /// <summary>
    /// What the wallet may still spend: the money posted in, minus the money posted and pending out. Money pending in
    /// is not counted until it is posted.
    /// </summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>When the wallet was made, in UTC.</summary>
    public required DateTime CreatedTime { get; init; }
}
