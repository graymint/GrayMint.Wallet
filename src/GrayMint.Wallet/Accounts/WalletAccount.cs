namespace GrayMint.Wallet.Accounts;

// An account keeps its own totals, so its balance is read without adding up its transfers. Only WalletService
// changes them, under the account's row lock; the reconciliation checks that they still add up.
public class WalletAccount
{
    public long AccountId { get; init; }
    public required int LedgerId { get; init; }
    public required string OwnerId { get; init; }
    public required string Code { get; init; }
    public required string Currency { get; init; }

    // the lowest the available amount may go: 0 for no credit, below 0 for a credit limit, null for no limit
    public required decimal? MinBalance { get; init; }

    public decimal DebitsPending { get; init; }
    public decimal DebitsPosted { get; init; }
    public decimal CreditsPending { get; init; }
    public decimal CreditsPosted { get; init; }
    public DateTime CreatedTime { get; init; }

    // credits minus debits, of what is posted
    public decimal Balance => CreditsPosted - DebitsPosted;

    // What may still be spent. A pending debit is held back; a pending credit is not there yet, so it can't be
    // spent before it is posted.
    public decimal Available => CreditsPosted - DebitsPosted - DebitsPending;
}
