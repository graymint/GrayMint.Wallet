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

    public async Task<OrderItemView[]> GetWalletTransactionsOfParticipantWallets(int appId,
        string participantWalletIds, DateTime? beginTime = null, DateTime? endTime = null, int? pageSize = null, int? pageNumber = null)
    {
        // Parse walletIds
        var walletIds = participantWalletIds.Split(",").Select(int.Parse).ToArray();

        // Get orders that contain transaction of participant wallets
        var orders = await _walletRepo.GetOrderItemsByWalletIds(appId, walletIds, beginTime, endTime, pageSize, pageNumber);

        return orders.Select(x => x.ToDto()).ToArray();
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
}
