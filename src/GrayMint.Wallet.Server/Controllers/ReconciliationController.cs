using GrayMint.Common.OpenApi;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Server.DtoConverters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrayMint.Wallet.Server.Controllers;

// Inside the namespace, so it is read before the package's, which names its report's parts the same.
using GrayMint.Wallet.Server.Dtos.Reconciliation;

/// <summary>
/// Reconciliation of the books: every wallet against its transfers and its wallet transactions, and every currency of
/// a tenant against zero.
/// </summary>
/// <remarks>
/// Nothing reconciles the books on its own. It reads every wallet and transfer of every tenant, so the caller asks for
/// the report when it wants one.
/// </remarks>
[ApiSection("Reconciliation", 30)]
[ApiController]
[Authorize]
[Route("/api/reconciliation")]
public class ReconciliationController(WalletReconciliationService reconciliationService) : ControllerBase
{
    /// <summary>Reconcile the books</summary>
    /// <remarks>
    /// Compares the books of every tenant and reports where they disagree, changing nothing: every wallet whose totals
    /// are not what its transfers add up to, or whose balances are not the ones its newest wallet transaction left, and
    /// every currency of a tenant whose wallets don't add up to zero. Each is a write that went round the wallet, or a
    /// fault in it. A wallet that a request is still writing is never reported.
    /// </remarks>
    /// <returns>Where the books disagree, which is nowhere on reconciled books.</returns>
    [HttpGet]
    public async Task<ReconciliationReport> Reconcile(CancellationToken cancellationToken)
    {
        var report = await reconciliationService.Reconcile(cancellationToken);
        return report.ToDto();
    }
}
