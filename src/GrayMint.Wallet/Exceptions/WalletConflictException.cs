namespace GrayMint.Wallet.Exceptions;

// The request can't be done in the ledger's present state, such as a key that names another transfer.
public class WalletConflictException : Exception
{
    public WalletConflictException(string message) : base(message)
    {
        // GrayMint's exception handler answers a request with it
        Data["HttpStatusCode"] = 409;
    }
}
