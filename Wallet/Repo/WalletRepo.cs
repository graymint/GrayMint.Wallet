using EWallet.DtoConverters;
using EWallet.Models;
using EWallet.Models.Views;
using EWallet.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Repo;

public class WalletRepo(WalletDbContext walletDbContext)
{
    public async Task AddEntity<TEntity>(TEntity entity) where TEntity : class
    {
        await walletDbContext.Set<TEntity>().AddAsync(entity);
    }

    public WalletDbContext GetDbContext()
    {
        return walletDbContext;
    }

    public Task AddEntities<TEntity>(TEntity[] entity) where TEntity : class
    {
        return walletDbContext.Set<TEntity>().AddRangeAsync(entity);
    }

    public async Task SaveChangesAsync()
    {
        await walletDbContext.SaveChangesAsync();
    }

    public async Task BeginTransaction()
    {
        await walletDbContext.Database.BeginTransactionAsync();
    }

    public Task CommitTransaction()
    {
        return walletDbContext.Database.CommitTransactionAsync();
    }

    public Task<AppModel> GetApp(int appId)
    {
        return walletDbContext.Apps
            .SingleAsync(a => a.AppId == appId);
    }

    public Task<CurrencyModel[]> GetCurrencies(int appId)
    {
        return walletDbContext.Currencies
            .Where(c => c.AppId == appId)
            .ToArrayAsync();
    }

    public Task<CurrencyModel> GetCurrency(int appId, int currencyId)
    {
        return walletDbContext.Currencies
            .Where(c => c.AppId == appId && c.CurrencyId == currencyId)
            .SingleAsync();
    }

    public Task<WalletModel> GetWallet(int appId, int walletId)
    {
        return walletDbContext.Wallets
            .Include(w => w.WalletBalances)
            .SingleAsync(w => w.AppId == appId && w.WalletId == walletId);
    }

    public async Task<long?> GetMaxWalletTransactionId()
    {
        return await walletDbContext.WalletTransactions
            .OrderByDescending(w => w.WalletTransactionId)
            .Select(x => x.WalletTransactionId)
            .FirstOrDefaultAsync();
    }

    public Task<WalletModel[]> GetWallets(int appId, List<int> walletIds)
    {
        return walletDbContext.Wallets
            .Include(w => w.WalletBalances)
            .Where(w => w.AppId == appId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .ToArrayAsync();
    }

    public Task<List<WalletBalanceModel>> GetWalletBalances(int appId, int currencyId, List<int> walletIds)
    {
        return walletDbContext.WalletBalances
            .Include(w => w.Wallet)
            .Where(w => w.Wallet!.AppId == appId && w.CurrencyId == currencyId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .ToListAsync();
    }

    public Task<List<WalletBalanceModel>> GetWalletBalancesWithoutTrack(int appId, int currencyId, List<int> walletIds)
    {
        return walletDbContext.WalletBalances
            .Include(w => w.Wallet)
            .Where(w => w.Wallet!.AppId == appId && w.CurrencyId == currencyId && walletIds.ToArray().Any(walletId => w.WalletId == walletId))
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<WalletBalanceModel?> FindWalletCurrency(int walletId, int currencyId)
    {
        return walletDbContext.WalletBalances
            .SingleOrDefaultAsync(x => x.CurrencyId == currencyId && x.WalletId == walletId);
    }

    public Task<OrderModel> GetOrder(int appId, Guid orderId)
    {
        return walletDbContext.Orders
            .Include(o => o.OrderItems)
            .SingleAsync(o => o.AppId == appId && o.OrderReferenceNumber == orderId);
    }

    public Task<OrderModel?> FindOrder(int appId, Guid orderId)
    {
        return walletDbContext.Orders
            .Include(x => x.OrderItems)
            .SingleOrDefaultAsync(o => o.AppId == appId && o.OrderReferenceNumber == orderId);
    }

    public Task<OrderModel> GetOrderFull(int appId, Guid orderId)
    {
        return walletDbContext.Orders
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

        var query = walletDbContext.OrderItems
            .Include(x => x.Order)
            .Where(x => x.Order!.AppId == appId)
            .Where(x => x.SenderWalletId == walletId || x.ReceiverWalletId == walletId)
            .Where(x => participantWalletId == null || (x.SenderWalletId == participantWalletId || x.ReceiverWalletId == participantWalletId))
            .Where(x => x.Order!.OrderTypeId == orderTypeId || orderTypeId == null)
            .Where(x => x.Order!.CreatedTime >= beginTime)
            .Where(x => x.Order!.CreatedTime < endTime)
            .Select(x => new
            {
                x.SenderWalletId,
                x.ReceiverWalletId,
                Status = OrderStatusConverter.ToDto(x.Order!.VoidedTime, x.Order.CapturedTime),
                x.Amount,
                x.Order.CurrencyId,
                x.Order.OrderReferenceNumber,
                x.Order.OrderId,
                x.OrderItemId,
                x.Order.OrderTypeId,
                x.Order.CreatedTime
            })
            .OrderByDescending(x => x.OrderId)
            .Skip(skip.Value)
            .Take(pageSize.Value);

        var result = await query.ToArrayAsync();
        return [.. result.Select(x => new OrderItemView
        {
            Amount = x.Amount,
            CreatedTime = x.CreatedTime,
            CurrencyId = x.CurrencyId,
            OrderId = x.OrderReferenceNumber,
            OrderItemId = x.OrderItemId,
            ReceiverWalletId = x.ReceiverWalletId,
            OrderTypeId = x.OrderTypeId,
            SenderWalletId = x.SenderWalletId,
            Status = x.Status
        })
        .OrderByDescending(x => x.CreatedTime)];
    }
}