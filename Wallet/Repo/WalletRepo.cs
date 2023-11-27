using EWallet.Models;
using EWallet.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Repo;

public class WalletRepo
{
    private readonly WalletDbContext _walletDbContext;

    public WalletRepo(WalletDbContext walletDbContext)
    {
        _walletDbContext = walletDbContext;
    }

    public async Task AddEntity<TEntity>(TEntity entity) where TEntity : class
    {
        await _walletDbContext.Set<TEntity>().AddAsync(entity);
    }

    public async Task AddEntities<TEntity>(TEntity[] entity) where TEntity : class
    {
        await _walletDbContext.Set<TEntity>().AddRangeAsync(entity);
    }

    public async Task SaveChangesAsync()
    {
        await _walletDbContext.SaveChangesAsync();
    }

    public async Task BeginTransaction()
    {
        await _walletDbContext.Database.BeginTransactionAsync();
    }

    public async Task CommitTransaction()
    {
        await _walletDbContext.Database.CommitTransactionAsync();
    }

    public async Task<AppModel> GetApp(int appId)
    {
        return await _walletDbContext.Apps
            .SingleAsync(a => a.AppId == appId);
    }

    public async Task<CurrencyModel[]> GetCurrencies(int appId)
    {
        return await _walletDbContext.Currencies
                     .Where(c => c.AppId == appId)
                     .ToArrayAsync();
    }

    public async Task<CurrencyModel> GetCurrency(int appId, int currencyId)
    {
        return await _walletDbContext.Currencies
            .Where(c => c.AppId == appId && c.CurrencyId == currencyId)
            .SingleAsync();
    }

    public async Task<WalletModel> GetWallet(int appId, int walletId)
    {
        return await _walletDbContext.Wallets
            .Include(w => w.WalletBalances)
            .SingleAsync(w => w.AppId == appId && w.WalletId == walletId);
    }

    public async Task<long?> GetMaxWalletTransactionId()
    {
        return await _walletDbContext.WalletTransactions
            .OrderByDescending(w => w.WalletTransactionId)
            .Select(x => x.WalletTransactionId)
            .FirstOrDefaultAsync();
    }

    public async Task<WalletModel[]> GetWallets(int appId, List<int> walletIds)
    {
        return await _walletDbContext.Wallets
            .Include(w => w.WalletBalances)
            .Where(w => w.AppId == appId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .ToArrayAsync();
    }

    public async Task<List<WalletBalanceModel>> GetWalletBalances(int appId, int currencyId, List<int> walletIds)
    {
        return await _walletDbContext.WalletBalances
            .Include(w => w.Wallet)
            .Where(w => w.Wallet!.AppId == appId && w.CurrencyId == currencyId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .ToListAsync();
    }

    public async Task<List<WalletBalanceModel>> GetWalletBalancesWithoutTrack(int appId, int currencyId, List<int> walletIds)
    {
        return await _walletDbContext.WalletBalances
            .Include(w => w.Wallet)
            .Where(w => w.Wallet!.AppId == appId && w.CurrencyId == currencyId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<WalletBalanceModel?> FindWalletCurrency(int walletId, int currencyId)
    {
        return await _walletDbContext.WalletBalances
            .SingleOrDefaultAsync(x => x.CurrencyId == currencyId && x.WalletId == walletId);
    }

    public async Task<WalletBalanceModel?> GetWalletBalance(int appId, int walletId, int currencyId)
    {
        return await _walletDbContext.WalletBalances
            .SingleOrDefaultAsync(
            b => b.WalletId == walletId && b.Wallet!.AppId == appId && b.CurrencyId == currencyId);
    }

    public async Task<OrderModel> GetOrder(int appId, Guid orderId)
    {
        return await _walletDbContext.Orders
            .Include(o => o.OrderItems)
            .SingleAsync(o => o.AppId == appId && o.OrderReferenceNumber == orderId);
    }

    public async Task<OrderModel> GetOrderFull(int appId, Guid orderId)
    {
        return await _walletDbContext.Orders
            .Include(x => x.App)
            .Include(o => o.OrderItems)!
            .ThenInclude(x => x.OrderTransactions)
            .SingleAsync(o => o.AppId == appId && o.OrderReferenceNumber == orderId);
    }

    public async Task<OrderModel[]> GetOrdersByWalletIds(int appId, int[] walletIds, DateTime? beginTime, DateTime? endTime, int? recordCount, int? recordIndex)
    {
        //var currentRecordIndex = recordIndex ?? 1;
        //var currentRecordCount = recordCount ?? 10;

        //beginTime ??= DateTime.MinValue;
        //endTime ??= DateTime.UtcNow;

        //return await _walletDbContext.Orders
        //    .Include(o => o.OrderTransactionModels)
        //    .Where(o => o.AppId == appId && o.OrderTransactionModels!.Any(t => walletIds.Any(i => i == t.WalletId) || walletIds.Any(i => i == t.ReferenceWalletTransaction!.WalletId)) && o.CreatedTime >= beginTime && o.CreatedTime < endTime)
        //    .OrderByDescending(t => t.CreatedTime)
        //    .Skip((currentRecordIndex - 1) * currentRecordCount)
        //    .Take(currentRecordCount)
        //    .ToArrayAsync();
        throw new NotImplementedException();
    }

}