using Asp.Versioning;
using EWallet.Dtos;
using EWallet.Server.Security;
using EWallet.Service;
using GrayMint.Authorization.PermissionAuthorizations;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[AuthorizeAppIdPermission(Permissions.AppReadWrite)]
[ApiController]
[ApiVersion("1")]
[Route("/api/v{version:apiVersion}/apps")]
public class AppsController(AppService appService,
    IHostEnvironment environment) : ControllerBase
{
    [AuthorizeAppIdPermission(Permissions.AppCreate)]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<App>> Create()
    {
        return StatusCode(StatusCodes.Status201Created, await appService.Create());
    }

    [HttpGet]
    public Task<App> Get(int appId)
    {
        return appService.Get(appId);
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