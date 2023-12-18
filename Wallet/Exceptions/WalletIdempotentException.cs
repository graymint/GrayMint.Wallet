namespace EWallet.Exceptions;

public class WalletIdempotentException : Exception
{
    public WalletIdempotentException(string message) : base(message) { }
}