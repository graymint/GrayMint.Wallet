namespace GrayMint.Wallet.Server.Dtos.Reconciliation;

/// <summary>
/// A tenant's wallets in one currency, which don't add up to zero: a wallet whose totals were changed round the
/// wallet, or a fault in it.
/// </summary>
public class CurrencyImbalance
{
    /// <summary>The tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>The currency, such as USD.</summary>
    public required string Currency { get; init; }

    /// <summary>
    /// What the tenant's wallets in the currency hold together. On reconciled books the money posted in is the money
    /// posted out, and the money pending in is the money pending out.
    /// </summary>
    public required WalletTotals Totals { get; init; }
}
