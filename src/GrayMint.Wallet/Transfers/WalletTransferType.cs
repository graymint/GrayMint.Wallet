namespace GrayMint.Wallet.Transfers;

// Stored by name: the database's checks and the reconciliation's SQL name these values.
/// <summary>What a transfer does.</summary>
public enum WalletTransferType
{
    /// <summary>Moves the amount at once.</summary>
    Posted,

    /// <summary>Holds the amount on the wallet it comes from until a post or a void settles it.</summary>
    Pending,

    /// <summary>Settles a pending transfer by moving the amount it held.</summary>
    PostPending,

    /// <summary>Settles a pending transfer by giving back the amount it held.</summary>
    VoidPending
}
