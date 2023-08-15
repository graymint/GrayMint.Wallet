using EWallet.Dtos;
using EWallet.Models;

namespace EWallet.DtoConverters;

public static class WalletConverter
{
    public static Wallet ToDto(this WalletModel model)
    {
        return new Wallet
        {
            WalletId = model.WalletId,
            CreatedTime = model.CreatedTime,
            Currencies = model.WalletBalances?.Select(wb => new CurrencyBalance
            {
                CurrencyId = wb.CurrencyId,
                Balance = wb.Balance,
                MinBalance = wb.MinBalance
            }).ToArray(),
        };
    }
}