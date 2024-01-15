namespace EWallet.Exceptions;

public class InsufficientBalanceException(string message) : Exception(message);