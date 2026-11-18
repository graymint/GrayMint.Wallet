namespace GrayMint.Wallet.Server.Accounts;

public class AccountCreateRequest
{
    // whose account it is, in the caller's own terms
    public required string OwnerId { get; init; }

    // which of the owner's accounts it is, such as its credit or its commission
    public required string Code { get; init; }

    // an upper-case code of letters and digits, such as USD
    public required string Currency { get; init; }

    // No default: a forgotten limit would be no limit, or none at all, and each is wrong for some account.
    public required decimal? MinBalance { get; init; }
}
