using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Persistence.Models;
using GrayMint.Wallet.Persistence.Views;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Persistence;

// The services' one way to the wallet's tables. Every query and write here runs in the session its service began
// (BeginRead, BeginWrite), never in one of its own: on a host's connection the session opens it through the host,
// and a session begun inside another would let go of the transaction the outer one began there.
// The context is the wallet's own, so a save writes only the row added here; the tracker is emptied after each, and
// every read is untracked.
internal sealed class WalletRepo(WalletDbContext dbContext, HostDatabase? hostDatabase)
{
    private readonly WalletDialect _dialect = WalletDialect.For(dbContext.Database);

    public Task<WalletSession> BeginRead(CancellationToken cancellationToken)
    {
        return WalletSession.Begin(dbContext, hostDatabase, isWrite: false, cancellationToken);
    }

    public Task<WalletSession> BeginWrite(CancellationToken cancellationToken)
    {
        return WalletSession.Begin(dbContext, hostDatabase, isWrite: true, cancellationToken);
    }

    public Task<WalletModel?> FindWallet(int tenantId, int walletId, CancellationToken cancellationToken)
    {
        return dbContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.WalletId == walletId, cancellationToken);
    }

    public Task<WalletModel?> FindWallet(int tenantId, string ownerReferenceKey, string walletCode, string currency,
        CancellationToken cancellationToken)
    {
        return dbContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OwnerReferenceKey == ownerReferenceKey &&
                                       x.WalletCode == walletCode && x.Currency == currency, cancellationToken);
    }

    public async Task<IReadOnlyList<WalletModel>> GetWalletsByOwner(int tenantId, string ownerReferenceKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.Wallets
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.OwnerReferenceKey == ownerReferenceKey)
            .OrderBy(x => x.WalletId)
            .ToArrayAsync(cancellationToken);
    }

    public Task<bool> HasWallet(WalletModel wallet, CancellationToken cancellationToken)
    {
        return dbContext.Wallets.AnyAsync(x =>
            x.TenantId == wallet.TenantId && x.OwnerReferenceKey == wallet.OwnerReferenceKey &&
            x.WalletCode == wallet.WalletCode && x.Currency == wallet.Currency, cancellationToken);
    }

    // false when another call made the same wallet meanwhile
    public async Task<bool> TryAddWallet(WalletModel wallet, CancellationToken cancellationToken)
    {
        dbContext.Wallets.Add(wallet);
        return await TrySave(() => HasWallet(wallet, cancellationToken), cancellationToken);
    }

    // One by one in id order, so two calls that need the same wallets never wait for each other in a circle, on
    // either database. The transaction holds the locks until it ends.
    public async Task<IReadOnlyList<WalletModel>> LockWallets(int tenantId, IEnumerable<int> walletIds,
        CancellationToken cancellationToken)
    {
        var wallets = new List<WalletModel>();
        foreach (var walletId in walletIds.Distinct().Order()) {
            var rows = await dbContext.Wallets
                .FromSqlRaw(_dialect.LockWalletSql, tenantId, walletId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            wallets.Add(rows.SingleOrDefault()
                         ?? throw new NotExistsException($"Wallet {walletId} is not in tenant {tenantId}."));
        }

        return wallets;
    }

    public Task SetMinAvailableBalance(int walletId, decimal? minAvailableBalance,
        CancellationToken cancellationToken)
    {
        return dbContext.Wallets
            .Where(x => x.WalletId == walletId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.MinAvailableBalance, minAvailableBalance),
                cancellationToken);
    }

    // By the change, not to a value read before, so the database adds it to what it holds under the lock.
    public async Task UpdateTotals(IEnumerable<WalletChange> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes.Where(x => x.IsChanged)) {
            await dbContext.Wallets
                .Where(x => x.WalletId == change.Wallet.WalletId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.PendingOut, x => x.PendingOut + change.PendingOut)
                        .SetProperty(x => x.PostedOut, x => x.PostedOut + change.PostedOut)
                        .SetProperty(x => x.PendingIn, x => x.PendingIn + change.PendingIn)
                        .SetProperty(x => x.PostedIn, x => x.PostedIn + change.PostedIn),
                    cancellationToken);
        }
    }

    public async Task<IReadOnlyList<TransferModel>> GetTransfersByExternalReferenceKeys(int tenantId,
        IReadOnlyList<string> externalReferenceKeys, CancellationToken cancellationToken)
    {
        return await dbContext.Transfers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ExternalReferenceKey != null &&
                        externalReferenceKeys.Contains(x.ExternalReferenceKey))
            .ToArrayAsync(cancellationToken);
    }

    public Task<TransferModel?> FindTransferByExternalReferenceKey(int tenantId, string externalReferenceKey,
        CancellationToken cancellationToken)
    {
        return dbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalReferenceKey == externalReferenceKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<TransferModel>> GetSettlements(int tenantId,
        IReadOnlyList<long> pendingTransferIds, CancellationToken cancellationToken)
    {
        return await dbContext.Transfers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PendingTransferId != null &&
                        pendingTransferIds.Contains(x.PendingTransferId.Value))
            .ToArrayAsync(cancellationToken);
    }

    public Task<TransferModel?> FindSettlement(int tenantId, long pendingTransferId,
        CancellationToken cancellationToken)
    {
        return dbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.PendingTransferId == pendingTransferId,
                cancellationToken);
    }

    // A wallet's statement, newest first; beforeTransferId goes on from the last one of a page. A wallet has one
    // transaction for each of its transfers, so the transfer's id orders them.
    public async Task<IReadOnlyList<WalletTransactionView>> GetWalletTransactions(int tenantId, int walletId,
        long? beforeTransferId, int limit, CancellationToken cancellationToken)
    {
        return await dbContext.WalletTransactions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WalletId == walletId &&
                        (beforeTransferId == null || x.TransferId < beforeTransferId))
            .Join(dbContext.Transfers, transaction => transaction.TransferId, transfer => transfer.TransferId,
                (transaction, transfer) => new WalletTransactionView {
                    WalletTransaction = transaction, Transfer = transfer
                })
            .OrderByDescending(x => x.WalletTransaction.TransferId)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    // false when another call took its external reference key, or settled its pending transfer, meanwhile
    public async Task<bool> TryAddTransfer(TransferModel transfer, CancellationToken cancellationToken)
    {
        dbContext.Transfers.Add(transfer);
        return await TrySave(() => IsTransferTaken(transfer, cancellationToken), cancellationToken);
    }

    // after their transfers, which give them their ids
    public async Task AddWalletTransactions(IEnumerable<WalletTransactionModel> transactions,
        CancellationToken cancellationToken)
    {
        dbContext.WalletTransactions.AddRange(transactions);
        try {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally {
            dbContext.ChangeTracker.Clear();
        }
    }

    // Every wallet whose stored totals are not what its transfers add up to, or whose balances are not the ones its
    // newest wallet transaction left (0, for a wallet without one); or the one wallet given, if it is such a wallet.
    public async Task<IReadOnlyList<WalletReconciliationView>> GetWalletDiscrepancies(int? walletId,
        CancellationToken cancellationToken)
    {
        var transfers = dbContext.Transfers;
        var transactions = dbContext.WalletTransactions;
        return await dbContext.Wallets
            .AsNoTracking()
            .Where(x => walletId == null || x.WalletId == walletId)
            .Select(wallet => new WalletReconciliationView {
                Wallet = wallet,
                LastPostedBalance = transactions
                    .Where(x => x.WalletId == wallet.WalletId)
                    .OrderByDescending(x => x.TransferId)
                    .Select(x => (decimal?)x.PostedBalance)
                    .FirstOrDefault(),
                LastAvailableBalance = transactions
                    .Where(x => x.WalletId == wallet.WalletId)
                    .OrderByDescending(x => x.TransferId)
                    .Select(x => (decimal?)x.AvailableBalance)
                    .FirstOrDefault(),
                FromTransfers = new WalletTotals {
                    PendingOut = transfers
                        .Where(x => x.FromWalletId == wallet.WalletId)
                        .Sum(x => x.Type == WalletTransferType.Pending ? x.Amount
                            : x.Type == WalletTransferType.Posted ? 0m : -x.Amount),
                    PostedOut = transfers
                        .Where(x => x.FromWalletId == wallet.WalletId)
                        .Sum(x => x.Type == WalletTransferType.Posted || x.Type == WalletTransferType.PostPending
                            ? x.Amount : 0m),
                    PendingIn = transfers
                        .Where(x => x.ToWalletId == wallet.WalletId)
                        .Sum(x => x.Type == WalletTransferType.Pending ? x.Amount
                            : x.Type == WalletTransferType.Posted ? 0m : -x.Amount),
                    PostedIn = transfers
                        .Where(x => x.ToWalletId == wallet.WalletId)
                        .Sum(x => x.Type == WalletTransferType.Posted || x.Type == WalletTransferType.PostPending
                            ? x.Amount : 0m)
                }
            })
            .Where(x => x.Wallet.PendingOut != x.FromTransfers.PendingOut ||
                        x.Wallet.PostedOut != x.FromTransfers.PostedOut ||
                        x.Wallet.PendingIn != x.FromTransfers.PendingIn ||
                        x.Wallet.PostedIn != x.FromTransfers.PostedIn ||
                        (x.LastPostedBalance ?? 0m) != x.Wallet.PostedIn - x.Wallet.PostedOut ||
                        (x.LastAvailableBalance ?? 0m) !=
                        x.Wallet.PostedIn - x.Wallet.PostedOut - x.Wallet.PendingOut)
            .OrderBy(x => x.Wallet.WalletId)
            .ToArrayAsync(cancellationToken);
    }

    // Each tenant's wallets in each currency, added up from their rows as they were at one moment.
    public async Task<IReadOnlyList<CurrencyTotalsView>> GetCurrencyTotals(CancellationToken cancellationToken)
    {
        return await dbContext.Wallets
            .FromSqlRaw(_dialect.WalletsAtOneMomentSql)
            .AsNoTracking()
            .GroupBy(x => new { x.TenantId, x.Currency })
            .Select(currency => new CurrencyTotalsView {
                TenantId = currency.Key.TenantId,
                Currency = currency.Key.Currency,
                WalletCount = currency.Count(),
                Totals = new WalletTotals {
                    PostedIn = currency.Sum(x => x.PostedIn),
                    PostedOut = currency.Sum(x => x.PostedOut),
                    PendingIn = currency.Sum(x => x.PendingIn),
                    PendingOut = currency.Sum(x => x.PendingOut)
                }
            })
            .OrderBy(x => x.TenantId)
            .ThenBy(x => x.Currency)
            .ToArrayAsync(cancellationToken);
    }

    // EF takes back a refused save to a savepoint of its own, so the transaction stays usable to tell a conflict
    // from any other refusal, which goes on as it came.
    private async Task<bool> TrySave(Func<Task<bool>> isConflict, CancellationToken cancellationToken)
    {
        try {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException) {
            dbContext.ChangeTracker.Clear();
            if (!WalletSession.IsTransactionEnded(dbContext.Database) && await isConflict())
                return false;
            throw;
        }
        finally {
            dbContext.ChangeTracker.Clear();
        }
    }

    private Task<bool> IsTransferTaken(TransferModel transfer, CancellationToken cancellationToken)
    {
        return transfer.ExternalReferenceKey != null
            ? dbContext.Transfers.AnyAsync(
                x => x.TenantId == transfer.TenantId && x.ExternalReferenceKey == transfer.ExternalReferenceKey,
                cancellationToken)
            : dbContext.Transfers.AnyAsync(x => x.PendingTransferId == transfer.PendingTransferId,
                cancellationToken);
    }
}
