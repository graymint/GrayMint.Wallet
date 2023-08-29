using EWallet.Dtos;
using EWallet.Models;

namespace EWallet.DtoConverters;

public static class AppConverter
{
    public static App ToDto(this AppModel model)
    {
        ArgumentNullException.ThrowIfNull(model.SystemWalletId);
        return new App
        {
            AppId = model.AppId,
            SystemWalletId = (int)model.SystemWalletId
        };
    }
}