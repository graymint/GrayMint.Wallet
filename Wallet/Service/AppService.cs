using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Models;
using EWallet.Repo;

namespace EWallet.Service;

public class AppService
{
    private readonly WalletRepo _walletRepo;
    private readonly WalletService _walletService;

    public AppService(WalletRepo walletRepo, WalletService walletService)
    {
        _walletRepo = walletRepo;
        _walletService = walletService;
    }

    public async Task<App> Create()
    {
        // Create App
        var app = new AppModel
        {
            CreatedTime = DateTime.UtcNow
        };

        await _walletRepo.BeginTransaction();

        // Save in db
        await _walletRepo.AddEntity(app);
        await _walletRepo.SaveChangesAsync();

        // create wallet
        var wallet = await _walletService.Create(app.AppId);

        // update app for system wallet
        app.SystemWalletId = wallet.WalletId;
        await _walletRepo.SaveChangesAsync();

        await _walletRepo.CommitTransaction();

        return app.ToDto();
    }

    public async Task<App> Get(int appId)
    {
        var app = await _walletRepo.GetApp(appId);
        return app.ToDto();
    }

    public async Task<AppModel> GetModel(int appId)
    {
        return await _walletRepo.GetApp(appId);
    }
}
