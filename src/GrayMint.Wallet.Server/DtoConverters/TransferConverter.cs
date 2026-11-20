using GrayMint.Wallet.Server.Dtos.Transfers;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Server.DtoConverters;

// The API's own shapes, apart from the package's, so a change to the package never changes the API unseen.
internal static class TransferConverter
{
    extension(WalletTransfer transfer)
    {
        public Transfer ToDto()
        {
            return new Transfer {
                TransferId = transfer.TransferId,
                TenantId = transfer.TenantId,
                Currency = transfer.Currency,
                Type = transfer.Type.ToDto(),
                ExternalReferenceKey = transfer.ExternalReferenceKey,
                FromWalletId = transfer.FromWalletId,
                ToWalletId = transfer.ToWalletId,
                Amount = transfer.Amount,
                PurposeCode = transfer.PurposeCode,
                PendingTransferId = transfer.PendingTransferId,
                CreatedTime = transfer.CreatedTime
            };
        }
    }

    // Value by value, never by a cast: a type the package adds fails here, not as a number the API never described.
    // The failure is the server's own, so not a NotSupportedException, which ServerFailureFilter answers with 400.
    extension(WalletTransferType type)
    {
        public TransferType ToDto()
        {
            return type switch {
                WalletTransferType.Posted => TransferType.Posted,
                WalletTransferType.Pending => TransferType.Pending,
                WalletTransferType.PostPending => TransferType.PostPending,
                WalletTransferType.VoidPending => TransferType.VoidPending,
                _ => throw new InvalidOperationException($"The transfer type {type} is not one of the API's.")
            };
        }
    }

    extension(TransferCreateRequest request)
    {
        public WalletTransferCreateRequest ToWalletRequest()
        {
            return new WalletTransferCreateRequest {
                ExternalReferenceKey = request.ExternalReferenceKey,
                FromWalletId = request.FromWalletId,
                ToWalletId = request.ToWalletId,
                Amount = request.Amount,
                IsPending = request.IsPending,
                PurposeCode = request.PurposeCode
            };
        }
    }
}
