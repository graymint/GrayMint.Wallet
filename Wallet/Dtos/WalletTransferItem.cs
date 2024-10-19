using EWallet.Models;

namespace EWallet.Dtos;

public sealed record WalletTransferItem
{
    public required ParticipantTransferItem ParticipantTransferItem { get; init; }
    public required int ActualReceiverWalletId { get; init; }
    public required long OrderItemId { get; init; }
    public required TransactionType? TransactionType { get; init; }
}