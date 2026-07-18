using EWallet.Dtos;
using EWallet.Server.Security;
using EWallet.Service;
using GrayMint.Authorization.PermissionAuthorizations;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[AuthorizeAppIdPermission(Permissions.AppReadWrite)]
[ApiController]
[Route("api/apps/{appId}/orders")]
public class OrdersController(OrderService orderService, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<Order> CreateOrder(int appId, CreateOrderRequest request, CancellationToken cancellationToken) {
        try
        {
            logger.LogInformation("CreateOrder.Start lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);

            using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);

            logger.LogInformation("CreateOrder.Finish lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);
            
            // create order
            var order = await orderService.CreateByLadder(appId, request);
            return order;
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "CreateOrder.Catch lock. OrderId: {OrderId}, Time {Time}:", request.OrderId, DateTime.UtcNow);
            throw;
        }
    }

    [HttpPost("batch")]
    public async Task<Order[]> CreateBatchOrders(int appId, CreateSingleTransferRequest[] requests, CancellationToken cancellationToken) {
        try
        {
            var orderIds = string.Join(", ", requests.Select(r => r.OrderId));
            logger.LogInformation("CreateBatchOrders.Start lock. OrderIds: {OrderIds}, Time {Time}:", orderIds, DateTime.UtcNow);

            using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);

            logger.LogInformation("CreateBatchOrders.Finish lock. OrderIds: {OrderIds}, Time {Time}:", orderIds, DateTime.UtcNow);

            // create batch orders
            var orders = await orderService.CreateBatchOrders(appId, requests);
            return orders;
        }
        catch (Exception e)
        {
            var orderIds = string.Join(", ", requests.Select(r => r.OrderId));
            logger.LogInformation(e, "CreateBatchOrders.Catch lock. OrderIds: {OrderIds}, Time {Time}:", orderIds, DateTime.UtcNow);
            throw;
        }
    }

    [HttpGet("{orderId:guid}")]
    public async Task<Order> GetOrder(int appId, Guid orderId) {
        var order = await orderService.GetOrder(appId, orderId);
        return order;
    }

    [HttpPost("{orderId:guid}/capture")]
    public async Task<Order> Capture(int appId, Guid orderId, CancellationToken cancellationToken) {
        try
        {
            logger.LogInformation("Capture.Start lock. OrderId: {OrderId}, Time {Time}:", orderId, DateTime.UtcNow);
            using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);
            logger.LogInformation("Capture.Finish lock. OrderId: {OrderId}, Time {Time}:", orderId, DateTime.UtcNow);

            // capture
            var order = await orderService.Capture(appId, orderId);
            return order;

        }
        catch (Exception e)
        {
            logger.LogInformation(e, "Capture.Catch lock. OrderId: {OrderId}, Time {Time}:", orderId, DateTime.UtcNow);

            throw;
        }

    }

    [HttpPost("{orderId:guid}/void")]
    public async Task<Order> Void(int appId, Guid orderId, CancellationToken cancellationToken) {
        using var appLock = await AsyncLock.LockAsync($"appId: {appId}", timeout: TimeSpan.FromMinutes(10), cancellationToken);

        var order = await orderService.Void(appId, orderId);
        return order;
    }
}