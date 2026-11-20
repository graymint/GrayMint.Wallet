using GrayMint.Wallet.Persistence.Views;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Wallets;

internal static class WalletTransactionConverter
{
    extension(WalletTransactionView view)
    {
        public WalletTransaction ToDto()
        {
            return new WalletTransaction {
                WalletId = WalletIds.Format(view.WalletTransaction.WalletId),
                Amount = view.WalletTransaction.Amount,
                PostedBalance = view.WalletTransaction.PostedBalance,
                AvailableBalance = view.WalletTransaction.AvailableBalance,
                Transfer = view.Transfer.ToDto()
            };
        }
    }
}
