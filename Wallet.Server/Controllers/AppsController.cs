using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("/api/v{version:apiVersion}/apps")]
public class AppsController : ControllerBase
{
    private readonly AppService _appService;

    public AppsController(AppService appService)
    {
        _appService = appService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<App>> Create()
    {
        return StatusCode(StatusCodes.Status201Created, await _appService.Create());
    }

    [HttpGet]
    public async Task<App> Get(int appId)
    {
        return await _appService.Get(appId);
    }
}