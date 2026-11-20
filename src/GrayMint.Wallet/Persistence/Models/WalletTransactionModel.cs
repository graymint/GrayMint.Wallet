namespace GrayMint.Wallet.Persistence.Models;

// A wallet's side of a transfer, written with it under the wallet's lock, so its balances are the ones the transfer
// left. Never changed or deleted, as its transfer isn't.
internal sealed class WalletTransactionModel
{
    public required int TenantId { get; init; }
    public required int WalletId { get; init; }
    public required long TransferId { get; init; }

    // the transfer's amount, below 0 on the wallet it comes from and above 0 on the one it goes to; the other way
    // round for a void, so a statement reads the hold undone
    public required decimal Amount { get; init; }

    public required decimal PostedBalance { get; init; }
    public required decimal AvailableBalance { get; init; }
}
