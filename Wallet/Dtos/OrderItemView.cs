using EWallet.Models;

namespace EWallet.Dtos;

public class OrderItemView
{
    public required Guid OrderId { get; init; }
    public required int CurrencyId { get; init; }
    public required int OrderTypeId { get; set; }
    public required long OrderItemId { get; init; }
    public required int SenderWalletId { get; init; }
    public required int ReceiverWalletId { get; init; }
    public required decimal Amount { get; init; }
}