namespace GrayMint.Wallet.Transfers;

// Stored by name: the database's checks and the reconciliation's SQL name these values.
public enum WalletTransferType
{
    Posted,
    Pending,
    PostPending,
    VoidPending
}
