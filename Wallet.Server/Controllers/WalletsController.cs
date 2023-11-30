using EWallet.Dtos;
using EWallet.Service;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId}/wallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletService _walletService;

    public WalletsController(WalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<Wallet>> CreateWallet(int appId)
    {
        return StatusCode(StatusCodes.Status201Created, await _walletService.Create(appId));
    }

    [HttpGet("{walletId:int}")]
    public async Task<Wallet> GetWallet(int appId, int walletId)
    {
        return await _walletService.Get(appId, walletId);
    }

    [HttpGet("transactions-by-participants")]
    public async Task<Order[]> GetWalletTransactionsOfParticipants(int appId, string participantWalletIds,
        DateTime? beginTime = null, DateTime? endTime = null, int? pageSize = null, int? pageNumber = null)
    {
        var order = await _walletService.GetWalletTransactionsOfParticipantWallets(appId, participantWalletIds, beginTime, endTime, pageSize, pageNumber);
        return order;
    }

    [HttpPost("{walletId}/min-balance")]
    public async Task<Wallet> SetMinBalance(int appId, int walletId, SetMinBalanceRequest request)
    {
        var wallet = await _walletService.SetMinBalance(appId, walletId, request);
        return wallet;
    }
}