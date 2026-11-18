using GrayMint.Authorization.PermissionAuthorizations;
using GrayMint.Wallet.Server.Security;
using GrayMint.Wallet.Server.Transfers;
using Microsoft.AspNetCore.Mvc;

namespace GrayMint.Wallet.Server.Accounts;

[ApiController]
[Route("/api/ledgers/{ledgerId}/accounts")]
public class AccountsController(WalletService walletService) : ControllerBase
{
    [HttpPost]
    [AuthorizePermission(WalletPermissions.Write)]
    public async Task<Account> Create(string ledgerId, AccountCreateRequest request,
        CancellationToken cancellationToken)
    {
        var account = await walletService.CreateAccount(ServerIds.ParseLedgerId(ledgerId), request.ToWalletRequest(),
            cancellationToken);
        return account.ToDto();
    }

    [HttpGet("{accountId}")]
    [AuthorizePermission(WalletPermissions.Read)]
    public async Task<Account> Get(string ledgerId, string accountId, CancellationToken cancellationToken)
    {
        var account = await walletService.GetAccount(ServerIds.ParseLedgerId(ledgerId),
            ServerIds.ParseLongId(accountId, "account"), cancellationToken);
        return account.ToDto();
    }

    [HttpGet]
    [AuthorizePermission(WalletPermissions.Read)]
    public async Task<IReadOnlyList<Account>> GetOwnerAccounts(string ledgerId, [FromQuery] string ownerId,
        CancellationToken cancellationToken)
    {
        var accounts = await walletService.GetOwnerAccounts(ServerIds.ParseLedgerId(ledgerId), ownerId,
            cancellationToken);
        return accounts.Select(x => x.ToDto()).ToArray();
    }

    [HttpPut("{accountId}/min-balance")]
    [AuthorizePermission(WalletPermissions.Write)]
    public async Task<Account> SetMinBalance(string ledgerId, string accountId, MinBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var account = await walletService.SetMinBalance(ServerIds.ParseLedgerId(ledgerId),
            ServerIds.ParseLongId(accountId, "account"), request.MinBalance, cancellationToken);
        return account.ToDto();
    }

    [HttpGet("{accountId}/transfers")]
    [AuthorizePermission(WalletPermissions.Read)]
    public async Task<IReadOnlyList<Transfer>> GetTransfers(string ledgerId, string accountId,
        [FromQuery] string? beforeTransferId, [FromQuery] int count, CancellationToken cancellationToken)
    {
        var transfers = await walletService.GetAccountTransfers(ServerIds.ParseLedgerId(ledgerId),
            ServerIds.ParseLongId(accountId, "account"), ServerIds.ParseLongIdOrNull(beforeTransferId, "transfer"),
            count, cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }
}
