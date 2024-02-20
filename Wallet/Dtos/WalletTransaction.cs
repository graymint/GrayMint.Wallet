namespace EWallet.Dtos;

public sealed class WalletTransaction
{
    public required long TransactionId { get; init; }
    public required int SenderWalletId { get; init; }
    public required int ReceiverWalletId { get; init; }
    public required decimal Amount { get; init; }
}