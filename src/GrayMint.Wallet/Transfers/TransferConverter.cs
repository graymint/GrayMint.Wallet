using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Transfers;

internal static class TransferConverter
{
    extension(TransferModel model)
    {
        public WalletTransfer ToDto()
        {
            return new WalletTransfer {
                TransferId = WalletIds.Format(model.TransferId),
                TenantId = WalletIds.Format(model.TenantId),
                Currency = model.Currency,
                Type = model.Type,
                ExternalReferenceKey = model.ExternalReferenceKey,
                FromWalletId = WalletIds.Format(model.FromWalletId),
                ToWalletId = WalletIds.Format(model.ToWalletId),
                Amount = model.Amount,
                PurposeCode = model.PurposeCode,
                PendingTransferId = model.PendingTransferId == null
                    ? null
                    : WalletIds.Format(model.PendingTransferId.Value),
                CreatedTime = model.CreatedTime
            };
        }
    }
}
