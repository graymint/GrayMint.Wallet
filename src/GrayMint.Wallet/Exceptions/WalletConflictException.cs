namespace GrayMint.Wallet.Exceptions;

/// <summary>
/// The tenant's present state refuses the request, such as an external reference key of another transfer, or a
/// wallet that is there already. The call changed nothing.
/// </summary>
public class WalletConflictException : Exception
{
    /// <summary>Makes the exception.</summary>
    /// <param name="message">What the tenant refused, and why.</param>
    public WalletConflictException(string message) : base(message)
    {
        // GrayMint's exception handler answers a request with it
        Data["HttpStatusCode"] = 409;
    }
}
