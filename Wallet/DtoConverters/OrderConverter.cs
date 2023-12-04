using EWallet.Dtos;
using EWallet.Models;

namespace EWallet.DtoConverters;
public static class OrderConverter
{
    public static Order ToDto(this OrderModel model)
    {
        // prepare order status
        var orderStatus = OrderStatusConverter.ToDto(model.VoidedTime, model.CapturedTime);

        ArgumentNullException.ThrowIfNull(model.OrderItems);
        return new Order
        {
            OrderId = model.OrderReferenceNumber,
            CurrencyId = model.CurrencyId,
            OrderTypeId = model.OrderTypeId,
            CreatedTime = model.CreatedTime,
            AuthorizedTime = model.CreatedTime,
            CapturedTime = model.CapturedTime,
            VoidedTime = model.VoidedTime,
            TransactionType = model.TransactionType,
            Status = orderStatus,
            Items = model.OrderItems.Select(x => new OrderItem
            {
                SenderWalletId = x.SenderWalletId,
                ReceiverWalletId = x.ReceiverWalletId,
                Amount = x.Amount,
                OrderItemId = x.OrderItemId
            }).ToArray()
        };
    }

    public static List<int> GetWalletIds(this ICollection<OrderItemModel> items)
    {
        // get list senders
        var list = items.Select(x => x.SenderWalletId)
            .Distinct()
            .ToList();

        // add receivers to list
        list.AddRange(items.Select(x => x.ReceiverWalletId)
            .Distinct()
            .ToList());

        return list
            .Distinct()
            .ToList();
    }
}