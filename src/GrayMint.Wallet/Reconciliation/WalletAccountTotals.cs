namespace GrayMint.Wallet.Reconciliation;

public class WalletAccountTotals
{
    public required decimal DebitsPending { get; init; }
    public required decimal DebitsPosted { get; init; }
    public required decimal CreditsPending { get; init; }
    public required decimal CreditsPosted { get; init; }
}
