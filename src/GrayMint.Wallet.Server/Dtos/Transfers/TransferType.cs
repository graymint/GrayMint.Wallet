namespace GrayMint.Wallet.Server.Dtos.Transfers;

/// <summary>What a transfer does.</summary>
public enum TransferType
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
