using Asp.Versioning;
using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/orders")]
public class OrdersController(OrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<Order> CreateOrder(int appId, CreateOrderRequest request)
    {
        using var orderLock = await WalletLock.LockOrder(request.OrderId.ToString());

        var order = await orderService.Create(appId, request);
        return order;
    }

    [HttpGet("{orderId:guid}")]
    public async Task<Order> GetOrder(int appId, Guid orderId)
    {
         var order = await orderService.GetOrder(appId, orderId);
         return order;
    }

    [HttpPost("{orderId:guid}/capture")]
    public async Task<Order> Capture(int appId, Guid orderId)
    {
        using var orderLock = await WalletLock.LockOrder(orderId.ToString());
        var order = await orderService.Capture(appId, orderId);
        return order;
    }

    [HttpPost("{orderId:guid}/void")]
    public async Task<Order> Void(int appId, Guid orderId)
    {
        using var orderLock = await WalletLock.LockOrder(orderId.ToString());
         var order = await orderService.Void(appId, orderId);
         return order;
    }
}