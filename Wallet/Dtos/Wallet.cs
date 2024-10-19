namespace EWallet.Dtos;

public sealed record Wallet
{
    public required int WalletId { get; init; }
    public CurrencyBalance[]? Currencies { get; set; }
    public required DateTime CreatedTime { get; init; }
}