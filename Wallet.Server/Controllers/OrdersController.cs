using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;   
    }

    [HttpPost]
    public async Task<Order> CreateOrder(int appId, CreateOrderRequest request)
    {
        var order = await _orderService.Create(appId, request);
        return order;
    }

    [HttpGet("{orderId:guid}")]
    public async Task<Order> GetOrder(int appId, Guid orderId)
    {
         var order = await _orderService.GetOrder(appId, orderId);
         return order;
    }

    [HttpPost("{orderId:guid}/capture")]
    public async Task<Order> Capture(int appId, Guid orderId)
    {
        var order = await _orderService.Capture(appId, orderId);
        return order;
    }

    [HttpPost("{orderId:guid}/void")]
    public async Task<Order> Void(int appId, Guid orderId)
    {
         var order = await _orderService.Void(appId, orderId);
         return order;
    }
}