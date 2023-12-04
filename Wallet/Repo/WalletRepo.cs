using EWallet.DtoConverters;
using EWallet.Models;
using EWallet.Models.Views;
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

    public async Task<OrderItemView[]> GetOrderItemsByWalletIds(int appId, int walletId, int? participantWalletId = null,
        DateTime? beginTime = null, DateTime? endTime = null, int? orderTypeId = null, int? pageSize = null, int? pageNumber = null)
    {
        const int days = 31;
        if (beginTime.HasValue && endTime.HasValue && (endTime.Value - beginTime.Value).TotalDays > days)
            throw new Exception("The report works for one month.");

        if (beginTime == null && endTime == null)
        {
            beginTime = DateTime.UtcNow.AddDays(-days);
            endTime = DateTime.UtcNow;
        }

        if (beginTime == null && endTime != null)
            beginTime = endTime.Value.AddDays(-days);

        if (beginTime != null && endTime == null)
            endTime = beginTime.Value.AddDays(days);
        if (beginTime > endTime) throw new Exception("BeginTime must be less than EndTime.");

        pageNumber ??= -1;
        pageSize = pageNumber is -1 ? int.MaxValue : pageSize is null or < 0 ? 101 : pageSize;
        var skip = pageNumber is -1 ? 0 : (pageNumber - 1) * pageSize;
        ArgumentNullException.ThrowIfNull(skip);

        var query = _walletDbContext.OrderItems
            .Include(x => x.Order)
            .Where(x => x.Order!.AppId == appId)
            .Where(x => x.SenderWalletId == walletId || x.ReceiverWalletId == walletId)
            .Where(x => participantWalletId == null || (x.SenderWalletId == participantWalletId || x.ReceiverWalletId == participantWalletId))
            .Where(x => x.Order!.OrderTypeId == orderTypeId || orderTypeId == null)
            .Where(x => x.Order!.CreatedTime >= beginTime)
            .Where(x => x.Order!.CreatedTime < endTime)
            .Select(x => new OrderItemView
            {
                SenderWalletId = x.SenderWalletId,
                ReceiverWalletId = x.ReceiverWalletId,
                Status = OrderStatusConverter.ToDto(x.Order!.VoidedTime, x.Order.CapturedTime),
                Amount = x.Amount,
                CurrencyId = x.Order.CurrencyId,
                OrderId = x.Order.OrderReferenceNumber,
                OrderItemId = x.OrderItemId,
                OrderTypeId = x.Order.OrderTypeId
            })
            .OrderByDescending(x => x.OrderId)
            .Skip(skip.Value)
            .Take(pageSize.Value);

        return await query.ToArrayAsync();
    }

}