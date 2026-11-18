namespace GrayMint.Wallet.Server.Accounts;

public class Account
{
    public required string AccountId { get; init; }
    public required string LedgerId { get; init; }
    public required string OwnerId { get; init; }
    public required string Code { get; init; }
    public required string Currency { get; init; }

    // the lowest the available amount may go: 0 for no credit, below 0 for a credit limit, null for no limit
    public required decimal? MinBalance { get; init; }

    public required decimal DebitsPending { get; init; }
    public required decimal DebitsPosted { get; init; }
    public required decimal CreditsPending { get; init; }
    public required decimal CreditsPosted { get; init; }

    // credits minus debits, of what is posted
    public required decimal Balance { get; init; }

    // what may still be spent: a pending credit is not there before it is posted
    public required decimal Available { get; init; }

    public required DateTime CreatedTime { get; init; }
}
