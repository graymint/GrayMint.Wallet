using EWallet.Models;

namespace EWallet.Dtos;

public class Order
{
    public required Guid OrderId { get; init; }
    public required int CurrencyId { get; init; }
    public required TransactionType TransactionType { get; init; }
    public required int OrderTypeId { get; set; }
    public required OrderStatus Status { get; init; }
    public required OrderItem[] Items { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required DateTime AuthorizedTime { get; init; }
    public DateTime? CapturedTime { get; set; }
    public DateTime? VoidedTime { get; set; }
}