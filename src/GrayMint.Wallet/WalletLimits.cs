namespace GrayMint.Wallet;

/// <summary>
/// The limits of what the wallet takes. A host that keeps one of these values in its own tables, such as the
/// external reference key of an order, sizes its column by them.
/// </summary>
public static class WalletLimits
{
    /// <summary>The most characters in a wallet's owner id.</summary>
    public const int MaxOwnerReferenceKeyLength = 100;

    /// <summary>The most characters in a wallet's or a transfer's code.</summary>
    public const int MaxCodeLength = 50;

    /// <summary>The most characters in a currency.</summary>
    public const int MaxCurrencyLength = 10;

    /// <summary>The most characters in a transfer's external reference key.</summary>
    public const int MaxExternalReferenceKeyLength = 100;

    /// <summary>The most decimal places of an amount or a limit.</summary>
    public const int AmountScale = 8;

    /// <summary>The most transfers one call makes, or settles.</summary>
    public const int MaxLinkedTransfers = 100;

    /// <summary>The most transfers on one page of a wallet's transfers.</summary>
    public const int MaxPageSize = 100;

    // A wallet's totals only grow, up to the columns' 20 whole digits, as many as a decimal holds with 8 places:
    // the database refuses a transfer that would take a total past them.
    /// <summary>The largest amount of one transfer, and the largest limit either way.</summary>
    public const decimal MaxAmount = 1_000_000_000_000_000m;

    // The database would round a ninth decimal away without a word.
    internal static void ThrowIfInvalidAmount(decimal amount, string paramName)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, AmountScale) != amount)
            throw new ArgumentOutOfRangeException(paramName, amount,
                $"An amount is above 0, at most {MaxAmount}, with at most {AmountScale} decimals.");
    }

    internal static void ThrowIfInvalidMinAvailableBalance(decimal minAvailableBalance, string paramName)
    {
        if (Math.Abs(minAvailableBalance) > MaxAmount ||
            decimal.Round(minAvailableBalance, AmountScale) != minAvailableBalance)
            throw new ArgumentOutOfRangeException(paramName, minAvailableBalance,
                $"A MinAvailableBalance is at most {MaxAmount} either way, with at most {AmountScale} decimals.");
    }

    // One spelling of a thing: no space at either end, which would make two of it, and SQL Server would not tell
    // apart.
    internal static void ThrowIfInvalidText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Trim() != value ||
            value.Any(char.IsControl))
            throw new ArgumentException(
                $"{paramName} has 1 to {maxLength} characters, with no space at either end.", paramName);
    }

    // Upper case only, so USD and usd are never two currencies.
    internal static void ThrowIfInvalidCurrency(string? currency)
    {
        if (string.IsNullOrEmpty(currency) || currency.Length > MaxCurrencyLength ||
            !currency.All(x => x is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new ArgumentException(
                $"A currency is 1 to {MaxCurrencyLength} upper-case letters and digits, such as USD.", nameof(currency));
    }
}
