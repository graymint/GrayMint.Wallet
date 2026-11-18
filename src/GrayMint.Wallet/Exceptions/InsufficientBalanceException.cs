namespace GrayMint.Wallet.Exceptions;

public class InsufficientBalanceException : Exception
{
    public long AccountId { get; }
    public decimal Available { get; }
    public decimal Amount { get; }
    public decimal MinBalance { get; }

    public InsufficientBalanceException(long accountId, decimal available, decimal amount, decimal minBalance)
        : base($"Account {accountId} can't pay {amount}: it has {available} to spend, and may not go below {minBalance}.")
    {
        AccountId = accountId;
        Available = available;
        Amount = amount;
        MinBalance = minBalance;

        // GrayMint's exception handler answers a request with it
        Data["HttpStatusCode"] = 409;
    }
}
