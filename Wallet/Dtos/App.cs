namespace EWallet.Dtos;

public sealed record App
{
    public required int AppId { get; init; }
    public required int SystemWalletId { get; init; }
}