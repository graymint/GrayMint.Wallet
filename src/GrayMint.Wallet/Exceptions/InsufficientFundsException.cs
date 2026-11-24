namespace GrayMint.Wallet.Exceptions;

/// <summary>
/// A transfer out of a wallet would take it below the lowest its available balance may go. The call changed nothing.
/// </summary>
public sealed class InsufficientFundsException : Exception
{
    /// <summary>The wallet that has too little to spend.</summary>
    public string WalletId { get; }

    /// <summary>What the wallet had to spend, after the linked transfers before the one refused.</summary>
    public decimal AvailableBalance { get; }

    /// <summary>The amount of the transfer refused.</summary>
    public decimal Amount { get; }

    /// <summary>The lowest the wallet's available balance may go.</summary>
    public decimal MinAvailableBalance { get; }

    /// <summary>Makes the exception.</summary>
    /// <param name="walletId">The wallet that has too little to spend.</param>
    /// <param name="availableBalance">What the wallet had to spend.</param>
    /// <param name="amount">The amount of the transfer refused.</param>
    /// <param name="minAvailableBalance">The lowest the wallet's available balance may go.</param>
    public InsufficientFundsException(string walletId, decimal availableBalance, decimal amount,
        decimal minAvailableBalance)
        : base($"Wallet {walletId} can't pay {amount}: it has {availableBalance} to spend, and may not go below " +
               $"{minAvailableBalance}.")
    {
        WalletId = walletId;
        AvailableBalance = availableBalance;
        Amount = amount;
        MinAvailableBalance = minAvailableBalance;

        // GrayMint's exception handler answers a request with it
        Data["HttpStatusCode"] = 409;
    }
}
