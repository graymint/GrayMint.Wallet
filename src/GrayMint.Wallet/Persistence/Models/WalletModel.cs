namespace GrayMint.Wallet.Persistence.Models;

// A wallet's row. Its totals change only under its lock, by the change a call adds to them; the reconciliation
// checks that they still add up to its transfers.
internal sealed class WalletModel
{
    public int WalletId { get; init; }
    public required int TenantId { get; init; }
    public required string OwnerReferenceKey { get; init; }
    public required string WalletCode { get; init; }
    public required string Currency { get; init; }

    // the lowest the available amount may go: 0 for no credit, below 0 for a credit limit, null for no limit
    public required decimal? MinAvailableBalance { get; init; }

    // A transfer moves the same amount on both its wallets, out of one and into the other, so over a tenant's wallets
    // in one currency, the root's included, PostedIn sums to PostedOut and PendingIn to PendingOut.

    // all the money ever posted into the wallet; it only grows
    public decimal PostedIn { get; init; }

    // all the money ever posted out of the wallet; it only grows
    public decimal PostedOut { get; init; }

    // Money held on its way in, not the wallet's yet. A pending transfer in adds to it; its post moves the amount to
    // PostedIn, and its void takes it off.
    public decimal PendingIn { get; init; }

    // Money held on its way out, which the wallet can no longer spend. A pending transfer out adds to it; its post
    // moves the amount to PostedOut, and its void gives it back.
    public decimal PendingOut { get; init; }

    // In minus out, of what is posted. Over a tenant's wallets in one currency it sums to zero: the root's is the
    // negative of all the others', as the money comes from it.
    public decimal PostedBalance => PostedIn - PostedOut;

    // What may still be spent. Money pending out is held back; money pending in is not there yet, so it can't be
    // spent before it is posted. It does not sum to zero: it takes the money pending out without the money pending in.
    public decimal AvailableBalance => PostedIn - PostedOut - PendingOut;
    
    public DateTime CreatedTime { get; init; }
}
