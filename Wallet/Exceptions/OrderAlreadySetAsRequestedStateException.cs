namespace EWallet.Exceptions;

public class OrderAlreadySetAsRequestedStateException(string message) : Exception(message);