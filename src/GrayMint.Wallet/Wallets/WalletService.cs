using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Wallets;

/// <summary>
/// The wallets of the tenants. A tenant is a partition of the host's that money never leaves, such as one store of a
/// platform, and its id is a number of the host's choosing. A wallet of another tenant is not there for a call.
/// </summary>
public sealed class WalletService
{
    /// <summary>
    /// The owner of every root wallet, which no other wallet has. A tenant's root wallet in a currency is the one money
    /// enters the tenant from and leaves it to (<see cref="GetOrCreateRootWallet"/>).
    /// </summary>
    public const string RootOwnerReferenceKey = "root";

    private const string RootWalletCode = "root";
    private readonly WalletRepo _repo;

    // made by the registration alone (WalletExtension.AddGrayMintWallet)
    internal WalletService(WalletRepo repo)
    {
        _repo = repo;
    }

    /// <summary>Makes a wallet, with nothing in it.</summary>
    /// <param name="tenantId">The tenant to make it in.</param>
    /// <param name="request">The wallet's owner, code, currency and limit.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The new wallet.</returns>
    /// <exception cref="ArgumentException">The request is not valid, or names <see cref="RootOwnerReferenceKey"/>.</exception>
    /// <exception cref="WalletConflictException">The tenant has a wallet of that owner, code and currency already.</exception>
    /// <exception cref="NotExistsException">The tenant's id is no number.</exception>
    public async Task<WalletInfo> Create(string tenantId, WalletCreateRequest request,
        CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        WalletLimits.ThrowIfInvalidText(request.OwnerReferenceKey, WalletLimits.MaxOwnerReferenceKeyLength, nameof(request.OwnerReferenceKey));
        WalletLimits.ThrowIfInvalidText(request.WalletCode, WalletLimits.MaxCodeLength, nameof(request.WalletCode));
        WalletLimits.ThrowIfInvalidCurrency(request.Currency);
        if (request.OwnerReferenceKey == RootOwnerReferenceKey)
            throw new ArgumentException($"The owner {RootOwnerReferenceKey} is the root wallets' alone; GetOrCreateRootWallet makes them.",
                nameof(request));

        if (request.MinAvailableBalance != null) {
            WalletLimits.ThrowIfInvalidMinAvailableBalance(request.MinAvailableBalance.Value,
                nameof(request.MinAvailableBalance));
            if (request.MinAvailableBalance > 0)
                throw new ArgumentException("A new wallet has nothing, so its MinAvailableBalance can't be above 0.",
                    nameof(request));
        }

        var wallet = new WalletModel {
            TenantId = dbTenantId,
            OwnerReferenceKey = request.OwnerReferenceKey,
            WalletCode = request.WalletCode,
            Currency = request.Currency,
            MinAvailableBalance = request.MinAvailableBalance
        };
        var conflict = new WalletConflictException(
            $"Tenant {tenantId} has a wallet {request.WalletCode} in {request.Currency} for {request.OwnerReferenceKey} already.");

        await using var session = await _repo.BeginWrite(cancellationToken);
        if (await _repo.HasWallet(wallet, cancellationToken))
            throw conflict;

        // the same wallet made by another call meanwhile
        if (!await _repo.TryAddWallet(wallet, cancellationToken))
            throw conflict;

        await session.Commit(cancellationToken);
        return wallet.ToDto();
    }

    /// <summary>
    /// Gets a tenant's root wallet in a currency, made at the first call: the wallet money enters the tenant from and
    /// leaves it to, such as a top-up paid from outside, or a refund paid back. It has no limit, as its balance is
    /// minus everything the tenant's other wallets in its currency hold.
    /// </summary>
    /// <param name="tenantId">The tenant whose root wallet it is.</param>
    /// <param name="currency">The root wallet's currency, such as USD.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The root wallet, with its totals as they are now.</returns>
    /// <exception cref="ArgumentException">The currency is not one a wallet could have.</exception>
    /// <exception cref="NotExistsException">The tenant's id is no number.</exception>
    public async Task<WalletInfo> GetOrCreateRootWallet(string tenantId, string currency, CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        WalletLimits.ThrowIfInvalidCurrency(currency);

        await using (await _repo.BeginRead(cancellationToken)) {
            if (await _repo.FindWallet(dbTenantId, RootOwnerReferenceKey, RootWalletCode, currency, cancellationToken) is { } found)
                return found.ToDto();
        }

        var root = new WalletModel {
            TenantId = dbTenantId,
            OwnerReferenceKey = RootOwnerReferenceKey,
            WalletCode = RootWalletCode,
            Currency = currency,
            MinAvailableBalance = null
        };

        // made by another call meanwhile: the insert is refused, and the other call's root is the one
        await using var session = await _repo.BeginWrite(cancellationToken);
        var rootWallet = await _repo.TryAddWallet(root, cancellationToken)
            ? root
            : await _repo.FindWallet(dbTenantId, RootOwnerReferenceKey, RootWalletCode, currency, cancellationToken)
              ?? throw new InvalidOperationException(
                  $"Tenant {tenantId}'s root wallet in {currency} was refused as there already, and is not.");

        await session.Commit(cancellationToken);
        return rootWallet.ToDto();
    }

    /// <summary>Gets a wallet, with its totals as they are now.</summary>
    /// <param name="tenantId">The wallet's tenant.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The wallet.</returns>
    /// <exception cref="NotExistsException">The wallet is not in the tenant.</exception>
    public async Task<WalletInfo> Get(string tenantId, string walletId, CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        var dbWalletId = WalletIds.ParseWalletId(walletId);

        await using var session = await _repo.BeginRead(cancellationToken);
        var wallet = await _repo.FindWallet(dbTenantId, dbWalletId, cancellationToken)
                      ?? throw new NotExistsException($"Wallet {walletId} is not in tenant {tenantId}.");
        return wallet.ToDto();
    }

    /// <summary>Finds a wallet by its owner, code and currency.</summary>
    /// <param name="tenantId">The wallet's tenant.</param>
    /// <param name="ownerReferenceKey">Whose wallet it is.</param>
    /// <param name="walletCode">Which of the owner's wallets it is.</param>
    /// <param name="currency">The wallet's currency.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The wallet, or null when the tenant has none of that owner, code and currency.</returns>
    /// <exception cref="ArgumentException">The owner, code or currency is not one a wallet could have.</exception>
    public async Task<WalletInfo?> Find(string tenantId, string ownerReferenceKey, string walletCode, string currency,
        CancellationToken cancellationToken)
    {
        // checked as a write checks them: SQL Server would find "k" for "k "
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        WalletLimits.ThrowIfInvalidText(ownerReferenceKey, WalletLimits.MaxOwnerReferenceKeyLength, nameof(ownerReferenceKey));
        WalletLimits.ThrowIfInvalidText(walletCode, WalletLimits.MaxCodeLength, nameof(walletCode));
        WalletLimits.ThrowIfInvalidCurrency(currency);

        await using var session = await _repo.BeginRead(cancellationToken);
        var wallet = await _repo.FindWallet(dbTenantId, ownerReferenceKey, walletCode, currency, cancellationToken);
        return wallet?.ToDto();
    }

    /// <summary>Gets an owner's wallets in a tenant, in the order they were made.</summary>
    /// <param name="tenantId">The wallets' tenant.</param>
    /// <param name="ownerReferenceKey">Whose wallets they are.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The owner's wallets; none when the owner has none in the tenant.</returns>
    public async Task<IReadOnlyList<WalletInfo>> GetByOwner(string tenantId, string ownerReferenceKey,
        CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        WalletLimits.ThrowIfInvalidText(ownerReferenceKey, WalletLimits.MaxOwnerReferenceKeyLength, nameof(ownerReferenceKey));

        await using var session = await _repo.BeginRead(cancellationToken);
        var wallets = await _repo.GetWalletsByOwner(dbTenantId, ownerReferenceKey, cancellationToken);
        return wallets.Select(x => x.ToDto()).ToArray();
    }

    /// <summary>Changes a wallet. A field the request leaves out keeps its value.</summary>
    /// <param name="tenantId">The wallet's tenant.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The wallet as it is now.</returns>
    /// <exception cref="ArgumentException">The new limit is not valid.</exception>
    /// <exception cref="NotExistsException">The wallet is not in the tenant.</exception>
    /// <exception cref="WalletConflictException">
    /// The wallet has less to spend than the new limit keeps, or is a root wallet, which has no limit.
    /// </exception>
    public async Task<WalletInfo> Update(string tenantId, string walletId, WalletUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        var dbWalletId = WalletIds.ParseWalletId(walletId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MinAvailableBalance?.Value is { } newLimit)
            WalletLimits.ThrowIfInvalidMinAvailableBalance(newLimit, nameof(request.MinAvailableBalance));

        // under the wallet's lock, so a payment can't spend meanwhile what the new limit keeps
        await using (var session = await _repo.BeginWrite(cancellationToken)) {
            var wallet = (await _repo.LockWallets(dbTenantId, [dbWalletId], cancellationToken)).Single();
            if (request.MinAvailableBalance != null) {
                var minAvailableBalance = request.MinAvailableBalance.Value;
                if (minAvailableBalance != null && wallet.OwnerReferenceKey == RootOwnerReferenceKey)
                    throw new WalletConflictException($"Wallet {walletId} is a root wallet, which has no limit.");

                if (minAvailableBalance != null && wallet.AvailableBalance < minAvailableBalance)
                    throw new WalletConflictException($"Wallet {walletId} has {wallet.AvailableBalance} to spend, " +
                                                      $"below the {minAvailableBalance} it would have to keep.");

                await _repo.SetMinAvailableBalance(dbWalletId, minAvailableBalance, cancellationToken);
            }

            await session.Commit(cancellationToken);
        }

        return await Get(tenantId, walletId, cancellationToken);
    }

    /// <summary>
    /// Gets a wallet's statement, newest first, a page at a time: its transactions, one for each of its transfers, with
    /// the amount below 0 when it went out and the balances the transfer left.
    /// </summary>
    /// <param name="tenantId">The wallet's tenant.</param>
    /// <param name="walletId">The wallet's id.</param>
    /// <param name="beforeTransferId">
    /// Null for the first page; for the next one, the transfer id of the last transaction of the page before.
    /// </param>
    /// <param name="limit">The most transactions a page has: 1 to <see cref="WalletLimits.MaxPageSize"/>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The wallet's transactions.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not 1 to <see cref="WalletLimits.MaxPageSize"/>.</exception>
    /// <exception cref="NotExistsException">The wallet is not in the tenant.</exception>
    public async Task<IReadOnlyList<WalletTransaction>> GetTransactions(string tenantId, string walletId,
        string? beforeTransferId, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > WalletLimits.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit), limit,
                $"A page has 1 to {WalletLimits.MaxPageSize} transactions.");

        var dbTenantId = WalletIds.ParseTenantId(tenantId);
        var dbWalletId = WalletIds.ParseWalletId(walletId);
        var dbBeforeTransferId = beforeTransferId == null ? (long?)null : WalletIds.ParseTransferId(beforeTransferId);

        await using var session = await _repo.BeginRead(cancellationToken);
        if (await _repo.FindWallet(dbTenantId, dbWalletId, cancellationToken) == null)
            throw new NotExistsException($"Wallet {walletId} is not in tenant {tenantId}.");

        var transactions = await _repo.GetWalletTransactions(dbTenantId, dbWalletId, dbBeforeTransferId, limit,
            cancellationToken);
        return transactions.Select(x => x.ToDto()).ToArray();
    }
}
