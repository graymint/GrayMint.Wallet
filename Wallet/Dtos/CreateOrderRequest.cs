using EWallet.Models;

namespace EWallet.Dtos;

public class CreateOrderRequest
{
    public required Guid OrderId { get; init; }
    public required int CurrencyId { get; init; }
    public required TransactionType TransactionType { get; init; }
    public required List<ParticipantTransferItem> ParticipantWallets { get; init; }
}