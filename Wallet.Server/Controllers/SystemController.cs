using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}")]
public class SystemController : Controller
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly WalletService _walletService;


    public SystemController(IHostEnvironment hostEnvironment, WalletService walletService)
    {
        _hostEnvironment = hostEnvironment;
        _walletService = walletService;
  
    }

    [HttpPost("clear-all")]
    public async Task ClearAll()
    {
        // check access
        if (_hostEnvironment.IsProduction())
            throw new UnauthorizedAccessException("This operation is not support in production.");

        await _walletService.ClearAll();
    }
}
