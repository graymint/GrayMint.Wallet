using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Models;
using EWallet.Models.Views;
using EWallet.Repo;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Service;

public class WalletService(WalletRepo walletRepo)
{
    public async Task<Wallet> Create(int appId)
    {
        // Create Wallet
        var walletModel = new WalletModel
        {
            AppId = appId,
            CreatedTime = DateTime.UtcNow
        };

        // Save to db
        await walletRepo.AddEntity(walletModel);
        await walletRepo.SaveChangesAsync();

        var wallet = await Get(appId, walletModel.WalletId);
        return wallet;
    }

    public async Task<Wallet> Get(int appId, int walletId)
    {
        // Get wallet from db
        var wallet = await walletRepo.GetWallet(appId, walletId);

        return wallet.ToDto();
    }

    public async Task<Wallet> SetMinBalance(int appId, int walletId, SetMinBalanceRequest request)
    {
        // get wallet to make sure wallet is correct
        await Get(appId, walletId);

        // get currency to make sure wallet is correct
        await GetCurrency(appId, request.CurrencyId);

        var minWalletCurrency = await walletRepo.FindWalletCurrency(walletId, request.CurrencyId);

        if (minWalletCurrency == null)
        {
            await walletRepo.AddEntity(new WalletBalanceModel
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

        await walletRepo.SaveChangesAsync();

        // prepare output
        return await Get(appId, walletId);
    }

    public async Task<OrderItemView[]> GetWalletTransactions(int appId,
        int walletId, int? participantWalletId = null, DateTime? beginTime = null, DateTime? endTime = null, int? orderTypeId = null, int? pageSize = null, int? pageNumber = null)
    {
        await Get(appId, walletId);

        if (participantWalletId is not null)
            await Get(appId, (int)participantWalletId);

        // Get orders that contain transaction of participant wallets
        var orders = await walletRepo.GetOrderItemsByWalletIds(
        appId, walletId, participantWalletId, beginTime, endTime, orderTypeId, pageSize, pageNumber);

        return orders;
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

        await walletRepo.BeginTransaction();

        // Save to db
        await walletRepo.AddEntity(currency);
        await walletRepo.SaveChangesAsync();

        // set minBalance for system wallet of the app
        var app = await walletRepo.GetApp(appId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);
        await SetMinBalance(appId, (int)app.SystemWalletId, new SetMinBalanceRequest
        {
            CurrencyId = currency.CurrencyId,
            MinBalance = -long.MaxValue
        });

        await walletRepo.CommitTransaction();

        return currency.CurrencyId;
    }

    public async Task<int[]> GetCurrencies(int appId)
    {
        // Validate app
        await walletRepo.GetApp(appId);

        var currencies = await walletRepo.GetCurrencies(appId);
        return currencies.Select(c => c.CurrencyId).ToArray();
    }

    public async Task<int> GetCurrency(int appId, int currencyId)
    {
        var currencyModel = await walletRepo.GetCurrency(appId, currencyId);
        return currencyModel.CurrencyId;
    }
    public async Task ClearAll()
    {
        await _walletRepo.BeginTransaction();
        await _walletRepo.DbContext().OrderItems.ExecuteDeleteAsync();
        await _walletRepo.DbContext().Wallets.ExecuteDeleteAsync();
        await _walletRepo.DbContext().WalletBalances.ExecuteDeleteAsync();
        await _walletRepo.DbContext().WalletTransactions.ExecuteDeleteAsync();
        await _walletRepo.DbContext().Orders.ExecuteDeleteAsync();
        await _walletRepo.DbContext().Currencies.ExecuteDeleteAsync();
        await _walletRepo.CommitTransaction();
        //await _walletRepo.DbContext().Apps.ExecuteDeleteAsync();
        //await _walletRepo.DbContext().TransactionTypes.ExecuteDeleteAsync();
    }
}
