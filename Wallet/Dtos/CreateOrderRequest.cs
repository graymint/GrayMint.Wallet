using EWallet.Models;

namespace EWallet.Dtos;

public sealed record CreateOrderRequest
{
    public required Guid OrderId { get; init; }
    public required int CurrencyId { get; init; }
    public required int OrderTypeId { get; set; }
    public required TransactionType TransactionType { get; init; }
    public required List<ParticipantTransferItem> ParticipantWallets { get; init; }
}