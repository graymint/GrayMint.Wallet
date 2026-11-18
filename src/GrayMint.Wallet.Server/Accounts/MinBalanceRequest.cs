namespace GrayMint.Wallet.Server.Accounts;

public class MinBalanceRequest
{
    // null for no limit
    public required decimal? MinBalance { get; init; }
}
