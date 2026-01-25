using EWallet.Server.Security;
using EWallet.Service;
using GrayMint.Authorization.PermissionAuthorizations;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[AuthorizeAppIdPermission(Permissions.AppReadWrite)]
[ApiController]
[Route("api/apps/{appId}/currencies")]
public class CurrenciesController(WalletService walletService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<int>> Create(int appId)
    {
        return StatusCode(StatusCodes.Status201Created, await walletService.CreateCurrency(appId));
    }

    [HttpGet]
    public Task<int[]> GetCurrencies(int appId)
    {
        return walletService.GetCurrencies(appId);
    }
}
