using EWallet.Dtos;
using EWallet.Models;

namespace EWallet.DtoConverters;
public static class OrderItemConverter
{
    public static OrderItemView ToDto(this OrderItemModel model)
    {
        ArgumentNullException.ThrowIfNull(model.Order);
        return new OrderItemView
        {
            OrderId = model.Order.OrderReferenceNumber,
            ReceiverWalletId = model.ReceiverWalletId,
            SenderWalletId = model.SenderWalletId,
            Amount = model.Amount,
            CurrencyId = model.Order.CurrencyId,
            OrderItemId = model.OrderItemId,
            OrderTypeId = model.Order.OrderTypeId
        };
    }
}
