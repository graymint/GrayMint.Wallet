using GrayMint.Wallet.Server.Dtos.Wallets;
using PackageWalletTransaction = GrayMint.Wallet.Wallets.WalletTransaction;

namespace GrayMint.Wallet.Server.DtoConverters;

// The API's own shapes, apart from the package's, so a change to the package never changes the API unseen. Both
// name it WalletTransaction: here the API's is the plain name, the package's the one with Package in front.
internal static class WalletTransactionConverter
{
    extension(PackageWalletTransaction transaction)
    {
        public WalletTransaction ToDto()
        {
            return new WalletTransaction {
                WalletId = transaction.WalletId,
                Amount = transaction.Amount,
                PostedBalance = transaction.PostedBalance,
                AvailableBalance = transaction.AvailableBalance,
                Transfer = transaction.Transfer.ToDto()
            };
        }
    }
}
