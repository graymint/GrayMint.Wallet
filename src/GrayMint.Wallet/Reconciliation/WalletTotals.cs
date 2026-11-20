namespace GrayMint.Wallet.Reconciliation;

/// <summary>The four totals of a wallet.</summary>
public class WalletTotals
{
    /// <summary>The money posted in.</summary>
    public required decimal PostedIn { get; init; }

    /// <summary>The money posted out.</summary>
    public required decimal PostedOut { get; init; }

    /// <summary>The money pending in.</summary>
    public required decimal PendingIn { get; init; }

    /// <summary>The money pending out.</summary>
    public required decimal PendingOut { get; init; }
}
