namespace EWallet.Dtos;

public sealed record SetMinBalanceRequest
{
    public required int CurrencyId { get; init; }
    public required decimal MinBalance { get; init; }
}