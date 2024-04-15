using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Models;
using EWallet.Repo;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Service;

public class AppService(WalletRepo walletRepo, WalletService walletService)
{
    public async Task<App> Create()
    {
        // Create App
        var app = new AppModel
        {
            CreatedTime = DateTime.UtcNow,
            AppId = 0,
            SystemWalletId = null
        };

        await walletRepo.BeginTransaction();

        // Save in db
        await walletRepo.AddEntity(app);
        await walletRepo.SaveChangesAsync();

        // create wallet
        var wallet = await walletService.Create(app.AppId);

        // update app for system wallet
        app.SystemWalletId = wallet.WalletId;
        await walletRepo.SaveChangesAsync();

        await walletRepo.CommitTransaction();

        return app.ToDto();
    }

    public async Task<App> Get(int appId)
    {
        var app = await walletRepo.GetApp(appId);
        return app.ToDto();
    }

    public Task<AppModel> GetModel(int appId)
    {
        return walletRepo.GetApp(appId);
    }

    public async Task ClearAll(int appId)
    {
        await walletRepo.GetDbContext().Database.BeginTransactionAsync();

        var app = await walletRepo.GetApp(appId);

        int? systemWalletId = null;
        await walletRepo.GetDbContext().Apps
            .Where(x => x.AppId == appId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(e => e.SystemWalletId, systemWalletId));

        await walletRepo.GetDbContext().Orders
            .Where(x => x.AppId == appId)
            .ExecuteDeleteAsync();

        await walletRepo.GetDbContext().Wallets
            .Where(x => x.AppId == appId)
            .ExecuteDeleteAsync();

        await walletRepo.GetDbContext().Currencies
            .Where(x => x.AppId == appId)
            .ExecuteDeleteAsync();

        // create wallet
        var wallet = await walletService.Create(app.AppId);

        // update app for system wallet
        app.SystemWalletId = wallet.WalletId;
        await walletRepo.SaveChangesAsync();

        await walletRepo.GetDbContext().Database.CommitTransactionAsync();
    }

    public async Task<string?> GetAuthorizationCode(int appId)
    {
        var app = await walletRepo.GetApp(appId);
        return app.AuthorizationCode;
    }

    public async Task UpdateAuthorizationCode(int appId, string authorizationCode)
    {
        // get max token id
        var app = await walletRepo.GetApp(appId);
        app.AuthorizationCode = authorizationCode;
        await walletRepo.SaveChangesAsync();
    }
}
