using Asp.Versioning;
using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("/api/v{version:apiVersion}/apps")]
public class AppsController(AppService appService,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<App>> Create()
    {
        return StatusCode(StatusCodes.Status201Created, await appService.Create());
    }

    [HttpGet]
    public async Task<App> Get(int appId)
    {
        return await appService.Get(appId);
    }

    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HttpPost("{appId}/clear-all")]
    public async Task<ActionResult> ClearAll(int appId)
    {
        // validate
        if (environment.IsProduction())
            throw new UnauthorizedAccessException("This operation is not support in production.");

        await appService.ClearAll(appId);
        return NoContent();
    }
}