using GrayMint.Common.OpenApi;
using GrayMint.Wallet.Server.DtoConverters;
using GrayMint.Wallet.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrayMint.Wallet.Server.Controllers;

// Inside the namespace, so it is read before the package's: from outside, Wallet would name the namespace
// GrayMint.Wallet, and the requests would be the package's as much as the API's.
using GrayMint.Wallet.Server.Dtos.Wallets;

/// <summary>
/// The wallets of a tenant. A wallet belongs to one tenant, one owner and one currency, and keeps the totals of its
/// transfers, posted and pending; a tenant has one for each owner, code and currency.
/// </summary>
[ApiSection("Wallets", 10)]
[ApiController]
[Authorize]
[Route("/api/tenants/{tenantId}/wallets")]
public class WalletsController(WalletService walletService) : ControllerBase
{
    /// <summary>Create a wallet</summary>
    /// <remarks>
    /// A new wallet has nothing in it. One of the same owner, code and currency in the tenant fails with 409.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="request">The wallet's owner, code, currency and limit.</param>
    /// <returns>The new wallet.</returns>
    [HttpPost]
    public async Task<Wallet> Create(string tenantId, WalletCreateRequest request,
        CancellationToken cancellationToken)
    {
        var wallet = await walletService.Create(tenantId, request.ToWalletRequest(), cancellationToken);
        return wallet.ToDto();
    }

    /// <summary>Get the tenant's root wallet</summary>
    /// <remarks>
    /// The wallet money enters the tenant from and leaves it to, in one currency, made at the first call: a top-up
    /// moves money from it, a refund back to it. It has no limit, as its balance is minus everything the tenant's other
    /// wallets in its currency hold.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="currency">The root wallet's currency, such as USD.</param>
    /// <returns>The root wallet, with its totals as they are now.</returns>
    [HttpGet("root")]
    public async Task<Wallet> GetOrCreateRootWallet(string tenantId, [FromQuery] string currency,
        CancellationToken cancellationToken)
    {
        var wallet = await walletService.GetOrCreateRootWallet(tenantId, currency, cancellationToken);
        return wallet.ToDto();
    }

    /// <summary>Get a wallet</summary>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <returns>The wallet, with its totals as they are now.</returns>
    [HttpGet("{walletId}")]
    public async Task<Wallet> Get(string tenantId, string walletId, CancellationToken cancellationToken)
    {
        var wallet = await walletService.Get(tenantId, walletId, cancellationToken);
        return wallet.ToDto();
    }

    /// <summary>List an owner's wallets</summary>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="ownerReferenceKey">Whose wallets they are.</param>
    /// <returns>The owner's wallets in the tenant, in the order they were made.</returns>
    [HttpGet]
    public async Task<IReadOnlyList<Wallet>> GetByOwner(string tenantId, [FromQuery] string ownerReferenceKey,
        CancellationToken cancellationToken)
    {
        var wallets = await walletService.GetByOwner(tenantId, ownerReferenceKey, cancellationToken);
        return wallets.Select(x => x.ToDto()).ToArray();
    }

    /// <summary>Update a wallet</summary>
    /// <remarks>
    /// A field left out keeps its value. A lower limit always takes. A higher one takes only while the wallet keeps to
    /// it, and fails with 409 otherwise, as a limit on a root wallet does.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <param name="request">The fields to change.</param>
    /// <returns>The wallet as it is now.</returns>
    [HttpPatch("{walletId}")]
    public async Task<Wallet> Update(string tenantId, string walletId, WalletUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var wallet = await walletService.Update(tenantId, walletId, request.ToWalletRequest(), cancellationToken);
        return wallet.ToDto();
    }

    /// <summary>List a wallet's transactions</summary>
    /// <remarks>
    /// The wallet's statement, newest first, a page at a time: one transaction for each of its transfers, with the
    /// amount below 0 when it went out and the balances the transfer left.
    /// </remarks>
    /// <param name="tenantId">The tenant's id.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <param name="beforeTransferId">
    /// Leave it out for the first page; for the next one, the transfer id of the last transaction of the page
    /// before.
    /// </param>
    /// <param name="limit">The most transactions a page has: 1 to 100.</param>
    /// <returns>The wallet's transactions.</returns>
    [HttpGet("{walletId}/transactions")]
    public async Task<IReadOnlyList<WalletTransaction>> GetTransactions(string tenantId, string walletId,
        [FromQuery] string? beforeTransferId, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var transactions = await walletService.GetTransactions(tenantId, walletId, beforeTransferId, limit,
            cancellationToken);
        return transactions.Select(x => x.ToDto()).ToArray();
    }
}
