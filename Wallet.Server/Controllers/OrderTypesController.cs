using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/order-types")]
public class OrderTypesController : ControllerBase
{
    private readonly WalletService _walletService;

    public OrderTypesController(WalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<int>> Create(int appId, string orderTypeName)
    {
        return StatusCode(StatusCodes.Status201Created, await _walletService.CreateOrderType(appId,orderTypeName));
    }

    [HttpGet]
    public async Task<OrderType[]> GetOrderTypes(int appId)
    {
        return await _walletService.GetOrderTypes(appId);
    }
}