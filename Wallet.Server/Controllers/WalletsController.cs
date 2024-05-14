using Asp.Versioning;
using EWallet.Dtos;
using EWallet.Models.Views;
using EWallet.Server.Security;
using EWallet.Service;
using GrayMint.Authorization.PermissionAuthorizations;
using Microsoft.AspNetCore.Mvc;

namespace EWallet.Server.Controllers;

[AuthorizeAppIdPermission(Permissions.AppReadWrite)]
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/apps/{appId:int}/wallets")]
public class WalletsController(WalletService walletService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<Wallet>> CreateWallet(int appId)
    {
        return StatusCode(StatusCodes.Status201Created, await walletService.Create(appId));
    }

    [HttpGet("{walletId:int}")]
    public Task<Wallet> GetWallet(int appId, int walletId)
    {
        return walletService.Get(appId, walletId);
    }

    [HttpGet]
    public Task<Wallet[]> GetWallets(int appId, string walletIds)
    {
        return walletService.GetWallets(appId, walletIds);
    }

    [HttpGet("transactions")]
    public async Task<OrderItemView[]> GetWalletTransactions(int appId, int walletId, int? participantWalletId = null,
        DateTime? beginTime = null, DateTime? endTime = null, int? orderTypeId = null, int? pageSize = null, int? pageNumber = null)
    {
        var orderItemViews = await walletService.GetWalletTransactions(
            appId, walletId, participantWalletId, beginTime, endTime, orderTypeId, pageSize, pageNumber);
        return orderItemViews;
    }

    [HttpPost("{walletId:int}/min-balance")]
    public async Task<Wallet> SetMinBalance(int appId, int walletId, SetMinBalanceRequest request)
    {
        var wallet = await walletService.SetMinBalance(appId, walletId, request);
        return wallet;
    }
}