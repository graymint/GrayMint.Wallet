using GrayMint.Authorization.PermissionAuthorizations;
using GrayMint.Wallet.Server.Security;
using Microsoft.AspNetCore.Mvc;

namespace GrayMint.Wallet.Server.Transfers;

// A caller saves its own order in its own database and the money here, apart. When it can't tell whether a
// request went through, it sends the same one again: the keys make the second one change nothing.
[ApiController]
[Route("/api/ledgers/{ledgerId}/transfers")]
public class TransfersController(WalletService walletService) : ControllerBase
{
    [HttpPost]
    [AuthorizePermission(WalletPermissions.Write)]
    public async Task<IReadOnlyList<Transfer>> Create(string ledgerId, IReadOnlyList<TransferRequest?> requests,
        CancellationToken cancellationToken)
    {
        // a body may hold null where a transfer belongs, which the model's checks let through
        var chain = requests.Select(x => x?.ToWalletRequest()
                                         ?? throw new ArgumentException("A transfer of the chain is missing.",
                                             nameof(requests)))
            .ToArray();

        var transfers = await walletService.CreateTransfers(ServerIds.ParseLedgerId(ledgerId), chain,
            cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    [HttpPost("post")]
    [AuthorizePermission(WalletPermissions.Write)]
    public async Task<IReadOnlyList<Transfer>> PostPending(string ledgerId, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var transfers = await walletService.PostPendingTransfers(ServerIds.ParseLedgerId(ledgerId), keys,
            cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    [HttpPost("void")]
    [AuthorizePermission(WalletPermissions.Write)]
    public async Task<IReadOnlyList<Transfer>> VoidPending(string ledgerId, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var transfers = await walletService.VoidPendingTransfers(ServerIds.ParseLedgerId(ledgerId), keys,
            cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    // by query, since a key may hold a slash
    [HttpGet("by-key")]
    [AuthorizePermission(WalletPermissions.Read)]
    public async Task<Transfer> Get(string ledgerId, [FromQuery] string key, CancellationToken cancellationToken)
    {
        var transfer = await walletService.GetTransfer(ServerIds.ParseLedgerId(ledgerId), key, cancellationToken);
        return transfer.ToDto();
    }
}
