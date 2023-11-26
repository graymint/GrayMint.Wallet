using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Models;
using EWallet.Repo;

namespace EWallet.Service;

public class WalletService
{
    private readonly WalletRepo _walletRepo;

    public WalletService(WalletRepo walletRepo)
    {
        _walletRepo = walletRepo;

    }

    public async Task<Wallet> Create(int appId)
    {
        // Create Wallet
        var walletModel = new WalletModel
        {
            AppId = appId,
            CreatedTime = DateTime.UtcNow
        };

        // Save to db
        await _walletRepo.AddEntity(walletModel);
        await _walletRepo.SaveChangesAsync();

        var wallet = await Get(appId, walletModel.WalletId);
        return wallet;
    }

    public async Task<Wallet> Get(int appId, int walletId)
    {
        // Get wallet from db
        var wallet = await _walletRepo.GetWallet(appId, walletId);

        return wallet.ToDto();
    }

    public async Task<Wallet> SetMinBalance(int appId, int walletId, SetMinBalanceRequest request)
    {
        // get wallet to make sure wallet is correct
        await Get(appId, walletId);

        // get currency to make sure wallet is correct
        await GetCurrency(appId, request.CurrencyId);

        var minWalletCurrency = await _walletRepo.FindWalletCurrency(walletId, request.CurrencyId);

        if (minWalletCurrency == null)
        {
            await _walletRepo.AddEntity(new WalletBalanceModel
            {
                WalletId = walletId,
                CurrencyId = request.CurrencyId,
                MinBalance = request.MinBalance,
                ModifiedTime = DateTime.UtcNow
            });
        }
        else
        {
            minWalletCurrency.MinBalance = request.MinBalance;
            minWalletCurrency.ModifiedTime = DateTime.UtcNow;
        }

        await _walletRepo.SaveChangesAsync();

        // prepare output
        return await Get(appId, walletId);
    }

    public async Task<Order[]> GetWalletTransactionsOfParticipantWallets(int appId,
        string participantWalletIds, DateTime? beginTime, DateTime? endTime, int? recordCount, int? recordIndex)
    {
        //// Parse walletIds
        //var walletIds = participantWalletIds.Split(",").Select(int.Parse).ToArray();

        //// Get orders that contain transaction of participant wallets
        //var orders = await _walletRepo.GetOrdersByWalletIds(appId, walletIds, beginTime, endTime, recordCount, recordIndex);

        //// List of orders
        //var result = new List<Order>();

        //// Get decreased records
        //foreach (var order in orders)
        //{
        //    var decreasedRecords = order.OrderTransactionModels!.Where(t => t.Amount < 0 && (walletIds.Any(i => i == t.WalletId) || walletIds.Any(i => i == t.ReferenceWalletTransaction!.WalletId)));

        //    // Create transactions
        //    var transactions = decreasedRecords.Select(transaction => new WalletTransaction(transaction.WalletTransactionId, transaction.WalletId, transaction.ReferenceWalletTransaction!.WalletId, -transaction.Amount, transaction.CreatedTime, order.AuthorizedTime, order.CapturedTime)).ToList();

        //    // Check if order is voided
        //    var voidOrder = order.VoidOrder;

        //    if (voidOrder is null)
        //    {
        //        result.Add(new Order(order.OrderId, order.CurrencyId, order.TransactionType, GetStatusOfOrder(order),
        //            transactions, null));
        //    }
        //    else
        //    {
        //        // Get void decreased records
        //        var voidDecreasedRecords = voidOrder!.OrderTransactionModels!.Where(t => t.Amount < 0 && (walletIds.Any(i => i == t.WalletId) || walletIds.Any(i => i == t.ReferenceWalletTransaction!.WalletId)));

        //        // Create void transactions
        //        var voidTransactions = voidDecreasedRecords.Select(transaction => new WalletTransaction(transaction.WalletTransactionId, transaction.WalletId, transaction.ReferenceWalletTransaction!.WalletId, -transaction.Amount, transaction.CreatedTime, voidOrder.AuthorizedTime, voidOrder.CapturedTime)).ToList();

        //        result.Add(new Order(order.OrderId, order.CurrencyId, order.TransactionType, GetStatusOfOrder(order),
        //            transactions, new Order(voidOrder.OrderId, voidOrder.CurrencyId, voidOrder.TransactionType, OrderStatus.Captured, voidTransactions, null)));
        //    }
        //}

        //return result.ToArray();
        throw new NotImplementedException();
    }

    public OrderStatus GetStatusOfOrder(OrderModel order)
    {
        if (order.VoidedTime is not null)
        {
            return OrderStatus.Voided;
        }

        if (
                (order.AuthorizedTime is null && order.CapturedTime is not null) ||
                (order.AuthorizedTime is not null && order.CapturedTime is not null)
            )
        {
            return OrderStatus.Captured;
        }

        if (order.AuthorizedTime is not null &&
            order.CapturedTime is null)
        {
            return OrderStatus.Authorized;
        }

        throw new Exception("Invalid dates for wallet transfer.");
    }

    public async Task<int> CreateCurrency(int appId)
    {
        // Create currency
        var currency = new CurrencyModel
        {
            AppId = appId
        };

        await _walletRepo.BeginTransaction();

        // Save to db
        await _walletRepo.AddEntity(currency);
        await _walletRepo.SaveChangesAsync();

        // set minBalance for system wallet of the app
        var app = await _walletRepo.GetApp(appId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);
        await SetMinBalance(appId, (int)app.SystemWalletId, new SetMinBalanceRequest
        {
            CurrencyId = currency.CurrencyId,
            MinBalance = -long.MaxValue
        });

        await _walletRepo.CommitTransaction();

        return currency.CurrencyId;
    }

    public async Task<int[]> GetCurrencies(int appId)
    {
        // Validate app
        await _walletRepo.GetApp(appId);

        var currencies = await _walletRepo.GetCurrencies(appId);
        return currencies.Select(c => c.CurrencyId).ToArray();
    }

    public async Task<int> GetCurrency(int appId, int currencyId)
    {
        var currencyModel = await _walletRepo.GetCurrency(appId, currencyId);
        return currencyModel.CurrencyId;
    }

    public async Task<int> CreateOrderType(int appId, string orderTypeName)
    {
        var orderType = new OrderTypeModel
        {
            AppId = appId,
            OrderTypeName = orderTypeName
        };

        await _walletRepo.AddEntity(orderType);
        await _walletRepo.SaveChangesAsync();

        return orderType.OrderTypeId;
    }
    public async Task<OrderType[]> GetOrderTypes(int appId)
    {
        var orderTypes = await _walletRepo.GetOrderTypes(appId);
        return orderTypes.Select(o => new OrderType
        {
            OrderTypeName = o.OrderTypeName,
            AppId = o.AppId,
            OrderTypeId = o.OrderTypeId
        }).ToArray();
    }
}
