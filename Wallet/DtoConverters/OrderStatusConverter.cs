using EWallet.Models;

namespace EWallet.DtoConverters;

public static class OrderStatusConverter
{
    public static OrderStatus ToDto(DateTime? voidedTime = null, DateTime? capturedTime = null)
    {
        // prepare order status
        OrderStatus orderStatus;

        if (voidedTime is not null)
            orderStatus = OrderStatus.Voided;
        else if (capturedTime is not null)
            orderStatus = OrderStatus.Captured;
        else
            orderStatus = OrderStatus.Authorized;
        return orderStatus;
    }
}