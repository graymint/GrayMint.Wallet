using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Transfers;

/// <summary>
/// The transfers of the tenants. Every new transfer carries the caller's external reference key: the same request sent again
/// changes nothing and gives back what the first one made, so a caller that can't tell whether a call went through
/// makes it again.
/// </summary>
public sealed class WalletTransferService
{
    private readonly WalletRepo _repo;

    // made by the registration alone (WalletExtension.AddGrayMintWallet)
    internal WalletTransferService(WalletRepo repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Makes linked transfers: in order, each seeing the ones before it, all of them or none. Sent again with the same
    /// external reference keys, they change nothing and come back as they were made.
    /// </summary>
    /// <param name="tenantId">The tenant of the transfers and of all their wallets.</param>
    /// <param name="requests">
    /// 1 to <see cref="WalletLimits.MaxLinkedTransfers"/> transfers, each with an external reference key of its own.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The transfers, in the order of the requests.</returns>
    /// <exception cref="ArgumentException">A request is not valid, or moves money from one currency to another.</exception>
    /// <exception cref="NotExistsException">A wallet is not in the tenant.</exception>
    /// <exception cref="InsufficientFundsException">
    /// A transfer would take the wallet it comes from below its <see cref="Wallets.WalletInfo.MinAvailableBalance"/>.
    /// </exception>
    /// <exception cref="WalletConflictException">An external reference key names another transfer.</exception>
    public async Task<IReadOnlyList<WalletTransfer>> Create(string tenantId,
        IReadOnlyList<WalletTransferCreateRequest> requests, CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        ThrowIfInvalidTransfers(requests);

        await using var session = await _repo.BeginWrite(cancellationToken);
        var walletIds = requests.SelectMany(x => new[] { FromWalletId(x), ToWalletId(x) }).ToArray();
        var wallets = await _repo.LockWallets(dbTenantId, walletIds, cancellationToken);

        // after the lock: transfers sent twice at once wait here for the first call, then find what it made
        var externalReferenceKeys = requests.Select(x => x.ExternalReferenceKey).ToArray();
        var madeBefore = await _repo.GetTransfersByExternalReferenceKeys(dbTenantId, externalReferenceKeys, cancellationToken);
        if (madeBefore.Count > 0)
            return GetTransfersMadeBefore(tenantId, requests, madeBefore);

        var changes = wallets.ToDictionary(x => x.WalletId, x => new WalletChange(x));
        var writes = new List<TransferWrite>();
        foreach (var request in requests) {
            var fromChange = changes[FromWalletId(request)];
            var toChange = changes[ToWalletId(request)];
            if (fromChange.Wallet.Currency != toChange.Wallet.Currency)
                throw new ArgumentException($"Transfer {request.ExternalReferenceKey} moves money from " +
                                            $"{fromChange.Wallet.Currency} to {toChange.Wallet.Currency}.",
                    nameof(requests));

            fromChange.MoveOut(request.Amount, request.IsPending);
            toChange.MoveIn(request.Amount, request.IsPending);
            var transfer = new TransferModel {
                TenantId = dbTenantId,
                Currency = fromChange.Wallet.Currency,
                Type = request.IsPending ? WalletTransferType.Pending : WalletTransferType.Posted,
                ExternalReferenceKey = request.ExternalReferenceKey,
                FromWalletId = fromChange.Wallet.WalletId,
                ToWalletId = toChange.Wallet.WalletId,
                Amount = request.Amount,
                PurposeCode = request.PurposeCode
            };
            writes.Add(new TransferWrite(transfer, fromChange, toChange));
        }

        await AddTransfers(tenantId, writes, cancellationToken);
        await _repo.UpdateTotals(changes.Values, cancellationToken);
        await session.Commit(cancellationToken);
        return writes.Select(x => x.Transfer.ToDto()).ToArray();
    }

    /// <summary>
    /// Posts pending transfers, all of them or none: each moves the amount it held. A transfer posted before comes back
    /// as it was.
    /// </summary>
    /// <param name="tenantId">The tenant of the transfers.</param>
    /// <param name="externalReferenceKeys">
    /// The external reference keys of 1 to <see cref="WalletLimits.MaxLinkedTransfers"/> pending transfers.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The posts, in the order of the keys.</returns>
    /// <exception cref="NotExistsException">A key names no transfer of the tenant.</exception>
    /// <exception cref="WalletConflictException">A transfer is not pending, or was voided.</exception>
    public Task<IReadOnlyList<WalletTransfer>> PostPending(string tenantId, IReadOnlyList<string> externalReferenceKeys,
        CancellationToken cancellationToken)
    {
        return Settle(tenantId, externalReferenceKeys, WalletTransferType.PostPending, cancellationToken);
    }

    /// <summary>
    /// Voids pending transfers, all of them or none: each gives back the amount it held. A transfer voided before comes
    /// back as it was.
    /// </summary>
    /// <param name="tenantId">The tenant of the transfers.</param>
    /// <param name="externalReferenceKeys">
    /// The external reference keys of 1 to <see cref="WalletLimits.MaxLinkedTransfers"/> pending transfers.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The voids, in the order of the keys.</returns>
    /// <exception cref="NotExistsException">A key names no transfer of the tenant.</exception>
    /// <exception cref="WalletConflictException">A transfer is not pending, or was posted.</exception>
    public Task<IReadOnlyList<WalletTransfer>> VoidPending(string tenantId, IReadOnlyList<string> externalReferenceKeys,
        CancellationToken cancellationToken)
    {
        return Settle(tenantId, externalReferenceKeys, WalletTransferType.VoidPending, cancellationToken);
    }

    /// <summary>Gets a transfer by its external reference key.</summary>
    /// <param name="tenantId">The transfer's tenant.</param>
    /// <param name="externalReferenceKey">The key the transfer was made with.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The transfer.</returns>
    /// <exception cref="NotExistsException">No transfer of the tenant has the key.</exception>
    public async Task<WalletTransfer> GetByExternalReferenceKey(string tenantId, string externalReferenceKey,
        CancellationToken cancellationToken)
    {
        // checked as a write checks it: a missing key would find a post or a void, which have none
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        WalletLimits.ThrowIfInvalidText(externalReferenceKey, WalletLimits.MaxExternalReferenceKeyLength, nameof(externalReferenceKey));

        await using var session = await _repo.BeginRead(cancellationToken);
        var transfer = await _repo.FindTransferByExternalReferenceKey(dbTenantId, externalReferenceKey, cancellationToken)
                       ?? throw new NotExistsException(
                           $"No transfer has the external reference key {externalReferenceKey} in tenant {tenantId}.");
        return transfer.ToDto();
    }

    /// <summary>Finds the post or the void of a pending transfer.</summary>
    /// <param name="tenantId">The transfer's tenant.</param>
    /// <param name="pendingTransferId">The pending transfer's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The post or the void, or null while the transfer is pending.</returns>
    public async Task<WalletTransfer?> FindSettlement(string tenantId, string pendingTransferId,
        CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        var dbPendingTransferId = WalletIds.ParseTransferId(pendingTransferId);

        await using var session = await _repo.BeginRead(cancellationToken);
        var settlement = await _repo.FindSettlement(dbTenantId, dbPendingTransferId, cancellationToken);
        return settlement?.ToDto();
    }

    // Settled one by one: a pending transfer settled the same way before comes back as it was, so a list sent again
    // changes nothing; one settled the other way is refused.
    private async Task<IReadOnlyList<WalletTransfer>> Settle(string tenantId, IReadOnlyList<string> externalReferenceKeys,
        WalletTransferType settlementType, CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        ThrowIfInvalidExternalReferenceKeys(externalReferenceKeys);

        await using var session = await _repo.BeginWrite(cancellationToken);
        var pendingTransfers = await _repo.GetTransfersByExternalReferenceKeys(dbTenantId, externalReferenceKeys,
            cancellationToken);
        foreach (var key in externalReferenceKeys) {
            var pendingTransfer = pendingTransfers.SingleOrDefault(x => x.ExternalReferenceKey == key)
                                  ?? throw new NotExistsException(
                                      $"No transfer has the external reference key {key} in tenant {tenantId}.");
            if (pendingTransfer.Type != WalletTransferType.Pending)
                throw new WalletConflictException($"Transfer {key} is no pending transfer: it was posted when it was made.");
        }

        var walletIds = pendingTransfers.SelectMany(x => new[] { x.FromWalletId, x.ToWalletId }).ToArray();
        var wallets = await _repo.LockWallets(dbTenantId, walletIds, cancellationToken);

        // after the lock, so a settlement made meanwhile is seen here
        var pendingTransferIds = pendingTransfers.Select(x => x.TransferId).ToArray();
        var settledBefore = await _repo.GetSettlements(dbTenantId, pendingTransferIds, cancellationToken);

        var changes = wallets.ToDictionary(x => x.WalletId, x => new WalletChange(x));
        var isPosted = settlementType == WalletTransferType.PostPending;
        var writes = new List<TransferWrite>();
        foreach (var key in externalReferenceKeys) {
            var pendingTransfer = pendingTransfers.Single(x => x.ExternalReferenceKey == key);
            var settlement = settledBefore.SingleOrDefault(x => x.PendingTransferId == pendingTransfer.TransferId);
            if (settlement != null) {
                if (settlement.Type != settlementType)
                    throw new WalletConflictException(settlement.Type == WalletTransferType.PostPending
                        ? $"Pending transfer {key} was posted already."
                        : $"Pending transfer {key} was voided already.");
                continue;
            }

            var fromChange = changes[pendingTransfer.FromWalletId];
            var toChange = changes[pendingTransfer.ToWalletId];
            fromChange.SettleOut(pendingTransfer.Amount, isPosted);
            toChange.SettleIn(pendingTransfer.Amount, isPosted);
            var settlementModel = new TransferModel {
                TenantId = dbTenantId,
                Currency = pendingTransfer.Currency,
                Type = settlementType,
                FromWalletId = pendingTransfer.FromWalletId,
                ToWalletId = pendingTransfer.ToWalletId,
                Amount = pendingTransfer.Amount,
                PurposeCode = pendingTransfer.PurposeCode,
                PendingTransferId = pendingTransfer.TransferId
            };
            writes.Add(new TransferWrite(settlementModel, fromChange, toChange));
        }

        await AddTransfers(tenantId, writes, cancellationToken);
        await _repo.UpdateTotals(changes.Values, cancellationToken);
        await session.Commit(cancellationToken);

        // in the order of the external reference keys, those settled before beside those settled now
        var all = settledBefore.Concat(writes.Select(x => x.Transfer)).ToArray();
        return externalReferenceKeys
            .Select(key => pendingTransfers.Single(x => x.ExternalReferenceKey == key).TransferId)
            .Select(pendingTransferId => all.Single(x => x.PendingTransferId == pendingTransferId).ToDto())
            .ToArray();
    }

    // One at a time, so the ids follow the transfers' order on either database; then their wallet transactions all
    // at once, as a wallet's are ordered by those ids.
    private async Task AddTransfers(string tenantId, IReadOnlyList<TransferWrite> writes,
        CancellationToken cancellationToken)
    {
        foreach (var transfer in writes.Select(x => x.Transfer)) {
            // An external reference key that another call took meanwhile without sharing a wallet with this one, so the
            // lock didn't keep them apart; or a pending transfer settled meanwhile.
            if (!await _repo.TryAddTransfer(transfer, cancellationToken))
                throw new WalletConflictException(transfer.ExternalReferenceKey != null
                    ? $"Tenant {tenantId} has a transfer {transfer.ExternalReferenceKey} already."
                    : $"Pending transfer {transfer.PendingTransferId} was settled meanwhile.");
        }

        await _repo.AddWalletTransactions(writes.SelectMany(x => x.CreateWalletTransactions()), cancellationToken);
    }

    // Each transfer the external reference keys name must be the one its request made, or the keys belong to other
    // transfers.
    private static IReadOnlyList<WalletTransfer> GetTransfersMadeBefore(string tenantId,
        IReadOnlyList<WalletTransferCreateRequest> requests, IReadOnlyList<TransferModel> madeBefore)
    {
        var transfers = new List<WalletTransfer>();
        foreach (var request in requests) {
            var transfer = madeBefore.SingleOrDefault(x => x.ExternalReferenceKey == request.ExternalReferenceKey);
            if (transfer == null || !IsMadeBy(transfer, request))
                throw new WalletConflictException(
                    $"Tenant {tenantId} has a transfer {madeBefore[0].ExternalReferenceKey} already, which is not one of " +
                    "these linked transfers as sent.");
            transfers.Add(transfer.ToDto());
        }

        return transfers;
    }

    private static bool IsMadeBy(TransferModel transfer, WalletTransferCreateRequest request)
    {
        var type = request.IsPending ? WalletTransferType.Pending : WalletTransferType.Posted;
        return transfer.Type == type &&
               transfer.FromWalletId == FromWalletId(request) &&
               transfer.ToWalletId == ToWalletId(request) &&
               transfer.Amount == request.Amount &&
               transfer.PurposeCode == request.PurposeCode;
    }

    // as numbers, so two spellings of one id are one wallet
    private static int FromWalletId(WalletTransferCreateRequest request)
    {
        return WalletIds.ParseWalletId(request.FromWalletId);
    }

    private static int ToWalletId(WalletTransferCreateRequest request)
    {
        return WalletIds.ParseWalletId(request.ToWalletId);
    }

    private static void ThrowIfInvalidTransfers(IReadOnlyList<WalletTransferCreateRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count is < 1 or > WalletLimits.MaxLinkedTransfers)
            throw new ArgumentException($"1 to {WalletLimits.MaxLinkedTransfers} linked transfers are made at once.",
                nameof(requests));

        foreach (var request in requests) {
            ArgumentNullException.ThrowIfNull(request, nameof(requests));
            WalletLimits.ThrowIfInvalidText(request.ExternalReferenceKey, WalletLimits.MaxExternalReferenceKeyLength,
                nameof(request.ExternalReferenceKey));
            if (request.PurposeCode != null)
                WalletLimits.ThrowIfInvalidText(request.PurposeCode, WalletLimits.MaxCodeLength, nameof(request.PurposeCode));
            if (FromWalletId(request) == ToWalletId(request))
                throw new ArgumentException($"Transfer {request.ExternalReferenceKey} has one wallet on both sides.",
                    nameof(requests));
            WalletLimits.ThrowIfInvalidAmount(request.Amount, nameof(request.Amount));
        }

        if (requests.Select(x => x.ExternalReferenceKey).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new ArgumentException("Two of the linked transfers have one external reference key.", nameof(requests));
    }

    private static void ThrowIfInvalidExternalReferenceKeys(IReadOnlyList<string> externalReferenceKeys)
    {
        ArgumentNullException.ThrowIfNull(externalReferenceKeys);
        if (externalReferenceKeys.Count is < 1 or > WalletLimits.MaxLinkedTransfers)
            throw new ArgumentException($"1 to {WalletLimits.MaxLinkedTransfers} pending transfers are settled at once.",
                nameof(externalReferenceKeys));

        foreach (var key in externalReferenceKeys)
            WalletLimits.ThrowIfInvalidText(key, WalletLimits.MaxExternalReferenceKeyLength, nameof(externalReferenceKeys));

        if (externalReferenceKeys.Distinct(StringComparer.Ordinal).Count() != externalReferenceKeys.Count)
            throw new ArgumentException("An external reference key is named twice.", nameof(externalReferenceKeys));
    }
}
