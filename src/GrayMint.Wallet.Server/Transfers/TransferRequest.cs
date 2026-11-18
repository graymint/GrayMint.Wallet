namespace GrayMint.Wallet.Server.Transfers;

public class TransferRequest
{
    // Unique in the ledger. The same request sent again with it changes nothing, so a caller that doesn't know
    // whether its request went through sends it again.
    public required string Key { get; init; }

    public required string DebitAccountId { get; init; }
    public required string CreditAccountId { get; init; }
    public required decimal Amount { get; init; }

    // held on the debit account until it is posted or voided
    public bool IsPending { get; init; }

    public string? Code { get; init; }
}
