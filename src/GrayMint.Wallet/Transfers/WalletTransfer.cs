namespace GrayMint.Wallet.Transfers;

// A transfer is never changed or deleted once written; the database refuses both. A mistake is put right by a
// transfer the other way, and a pending one is settled by a second row, its post or its void.
public class WalletTransfer
{
    public long TransferId { get; init; }
    public required int LedgerId { get; init; }
    public required string Currency { get; init; }
    public required WalletTransferType Type { get; init; }

    // the caller's idempotency key, unique in its ledger; a post or a void has none, as it is one per pending
    public string? Key { get; init; }

    public required long DebitAccountId { get; init; }
    public required long CreditAccountId { get; init; }
    public required decimal Amount { get; init; }

    // what the transfer is for, in the host's own terms
    public string? Code { get; init; }

    // the pending transfer that a post or a void settles
    public long? PendingTransferId { get; init; }

    public DateTime CreatedTime { get; init; }
}
