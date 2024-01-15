using Asp.Versioning;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/currencies")]
public class CurrenciesController(WalletService walletService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<int>> Create(int appId)
    {
        return StatusCode(StatusCodes.Status201Created, await walletService.CreateCurrency(appId));
    }

    [HttpGet]
    public async Task<int[]> GetCurrencies(int appId)
    {
        return await walletService.GetCurrencies(appId);
    }
}
