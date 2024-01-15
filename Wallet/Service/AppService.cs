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
            CreatedTime = DateTime.UtcNow
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

    public async Task<AppModel> GetModel(int appId)
    {
        return await walletRepo.GetApp(appId);
    }

    public Task ClearAll(int appId)
    {
        return walletRepo.GetDbContext().Apps
            .Where(x => x.AppId == appId)
            .ExecuteDeleteAsync();
    }
}
