namespace EWallet.Exceptions;

public class InvalidTransactionTypeException : Exception
{
    public InvalidTransactionTypeException(string message) : base(message) { }
}