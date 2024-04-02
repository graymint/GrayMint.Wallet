using Asp.Versioning;
using EWallet.Dtos;
using EWallet.Service;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/orders")]
public class OrdersController(OrderService orderService, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<Order> CreateOrder(int appId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogWarning("Start lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);
            
            using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10));

            logger.LogWarning("Finish lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);


            // create order
            var order = await orderService.Create(appId, request);
            return order;
        }
        catch (Exception e)
        {
            logger.LogWarning("Catch lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);
            Console.WriteLine(e);
            throw;
        }
    }

    [HttpGet("{orderId:guid}")]
    public async Task<Order> GetOrder(int appId, Guid orderId)
    {
        var order = await orderService.GetOrder(appId, orderId);
        return order;
    }

    [HttpPost("{orderId:guid}/capture")]
    public async Task<Order> Capture(int appId, Guid orderId, CancellationToken cancellationToken)
    {
        using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);

        // capture
        var order = await orderService.Capture(appId, orderId);
        return order;
    }

    [HttpPost("{orderId:guid}/void")]
    public async Task<Order> Void(int appId, Guid orderId, CancellationToken cancellationToken)
    {
        using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);

        var order = await orderService.Void(appId, orderId);
        return order;
    }
}