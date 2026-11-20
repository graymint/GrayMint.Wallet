using GrayMint.Common.OpenApi;
using GrayMint.Wallet.Server.DtoConverters;
using GrayMint.Wallet.Server.Dtos.Transfers;
using GrayMint.Wallet.Transfers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrayMint.Wallet.Server.Controllers;

/// <summary>
/// The transfers of a tenant. A transfer moves an amount from one wallet to another of its tenant and currency, and
/// is never changed: a mistake is put right by a transfer the other way.
/// </summary>
/// <remarks>
/// A caller keeps its orders in its own database and its money here. When it can't tell whether a request went
/// through, it sends the same one again: the external reference keys make the second one change nothing.
/// </remarks>
[ApiSection("Transfers", 20)]
[ApiController]
[Authorize]
[Route("/api/tenants/{tenantId}/transfers")]
public class TransfersController(WalletTransferService transferService) : ControllerBase
{
    /// <summary>Make linked transfers</summary>
    /// <remarks>
    /// The transfers are made in order, each seeing the ones before it, all of them or none. Sent again with the same
    /// external reference keys, they change nothing and come back as they were made. A transfer that would take a
    /// wallet below its limit fails with 409, and so does a key of another transfer.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="requests">1 to 100 transfers, each with an external reference key of its own.</param>
    /// <returns>The transfers, in the order of the requests.</returns>
    [HttpPost]
    public async Task<IReadOnlyList<Transfer>> Create(string tenantId,
        IReadOnlyList<TransferCreateRequest?> requests, CancellationToken cancellationToken)
    {
        // a body may hold null where a transfer belongs, which the model's checks let through
        var walletRequests = requests
            .Select(x => x?.ToWalletRequest()
                         ?? throw new ArgumentException("One of the linked transfers is missing.", nameof(requests)))
            .ToArray();

        var transfers = await transferService.Create(tenantId, walletRequests, cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    /// <summary>Post pending transfers</summary>
    /// <remarks>
    /// Each moves the amount it held, all of them or none. A transfer posted before comes back as it was; one that was
    /// voided fails with 409.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="externalReferenceKeys">The external reference keys of 1 to 100 pending transfers.</param>
    /// <returns>The posts, in the order of the keys.</returns>
    [HttpPost("post")]
    public async Task<IReadOnlyList<Transfer>> PostPending(string tenantId, IReadOnlyList<string> externalReferenceKeys,
        CancellationToken cancellationToken)
    {
        var transfers = await transferService.PostPending(tenantId, externalReferenceKeys, cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    /// <summary>Void pending transfers</summary>
    /// <remarks>
    /// Each gives back the amount it held, all of them or none. A transfer voided before comes back as it was; one that
    /// was posted fails with 409.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="externalReferenceKeys">The external reference keys of 1 to 100 pending transfers.</param>
    /// <returns>The voids, in the order of the keys.</returns>
    [HttpPost("void")]
    public async Task<IReadOnlyList<Transfer>> VoidPending(string tenantId, IReadOnlyList<string> externalReferenceKeys,
        CancellationToken cancellationToken)
    {
        var transfers = await transferService.VoidPending(tenantId, externalReferenceKeys, cancellationToken);
        return transfers.Select(x => x.ToDto()).ToArray();
    }

    // by query, since an external reference key may hold a slash
    /// <summary>Get a transfer by its external reference key</summary>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="externalReferenceKey">The key the transfer was made with.</param>
    /// <returns>The transfer.</returns>
    [HttpGet]
    public async Task<Transfer> GetByExternalReferenceKey(string tenantId, [FromQuery] string externalReferenceKey,
        CancellationToken cancellationToken)
    {
        var transfer = await transferService.GetByExternalReferenceKey(tenantId, externalReferenceKey,
            cancellationToken);
        return transfer.ToDto();
    }
}
