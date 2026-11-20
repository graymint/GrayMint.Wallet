using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Wallets;

internal static class WalletConverter
{
    extension(WalletModel model)
    {
        public WalletInfo ToDto()
        {
            return new WalletInfo {
                WalletId = WalletIds.Format(model.WalletId),
                TenantId = WalletIds.Format(model.TenantId),
                OwnerReferenceKey = model.OwnerReferenceKey,
                WalletCode = model.WalletCode,
                Currency = model.Currency,
                MinAvailableBalance = model.MinAvailableBalance,
                PostedIn = model.PostedIn,
                PostedOut = model.PostedOut,
                PendingIn = model.PendingIn,
                PendingOut = model.PendingOut,
                PostedBalance = model.PostedBalance,
                AvailableBalance = model.AvailableBalance,
                CreatedTime = model.CreatedTime
            };
        }
    }
}
