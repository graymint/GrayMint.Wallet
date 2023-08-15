using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/currencies")]
public class CurrenciesController : ControllerBase
{
    private readonly WalletService _walletService;

    public CurrenciesController(WalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<int>> Create(int appId)
    {
        return StatusCode(StatusCodes.Status201Created, await _walletService.CreateCurrency(appId));
    }

    [HttpGet]
    public async Task<int[]> GetCurrencies(int appId)
    {
        return await _walletService.GetCurrencies(appId);
    }
}