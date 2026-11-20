using GrayMint.Wallet.Wallets;
using PackageWalletCreateRequest = GrayMint.Wallet.Wallets.WalletCreateRequest;
using PackageWalletUpdateRequest = GrayMint.Wallet.Wallets.WalletUpdateRequest;

namespace GrayMint.Wallet.Server.DtoConverters;

// Inside the namespace, so it is read before the package's: from outside, Wallet would name the namespace
// GrayMint.Wallet, and the requests would be the package's as much as the API's.
using GrayMint.Wallet.Server.Dtos.Wallets;

// The API's own shapes, apart from the package's, so a change to the package never changes the API unseen. Both
// name their requests the same: here the API's is the plain name, the package's the one with Package in front.
internal static class WalletConverter
{
    extension(WalletInfo wallet)
    {
        public Wallet ToDto()
        {
            return new Wallet {
                WalletId = wallet.WalletId,
                TenantId = wallet.TenantId,
                OwnerReferenceKey = wallet.OwnerReferenceKey,
                WalletCode = wallet.WalletCode,
                Currency = wallet.Currency,
                MinAvailableBalance = wallet.MinAvailableBalance,
                PostedIn = wallet.PostedIn,
                PostedOut = wallet.PostedOut,
                PendingIn = wallet.PendingIn,
                PendingOut = wallet.PendingOut,
                PostedBalance = wallet.PostedBalance,
                AvailableBalance = wallet.AvailableBalance,
                CreatedTime = wallet.CreatedTime
            };
        }
    }

    extension(WalletCreateRequest request)
    {
        public PackageWalletCreateRequest ToWalletRequest()
        {
            return new PackageWalletCreateRequest {
                OwnerReferenceKey = request.OwnerReferenceKey,
                WalletCode = request.WalletCode,
                Currency = request.Currency,
                MinAvailableBalance = request.MinAvailableBalance
            };
        }
    }

    extension(WalletUpdateRequest request)
    {
        public PackageWalletUpdateRequest ToWalletRequest()
        {
            return new PackageWalletUpdateRequest {
                MinAvailableBalance = request.MinAvailableBalance
            };
        }
    }
}
