using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Server.Transfers;

public static class TransferConverter
{
    extension(WalletTransfer transfer)
    {
        public Transfer ToDto()
        {
            return new Transfer {
                TransferId = ServerIds.Format(transfer.TransferId),
                LedgerId = ServerIds.Format(transfer.LedgerId),
                Currency = transfer.Currency,
                Type = transfer.Type,
                Key = transfer.Key,
                DebitAccountId = ServerIds.Format(transfer.DebitAccountId),
                CreditAccountId = ServerIds.Format(transfer.CreditAccountId),
                Amount = transfer.Amount,
                Code = transfer.Code,
                PendingTransferId = transfer.PendingTransferId == null
                    ? null
                    : ServerIds.Format(transfer.PendingTransferId.Value),
                CreatedTime = transfer.CreatedTime
            };
        }
    }

    extension(TransferRequest request)
    {
        // an account id that is no number names no account, as one that is not in the ledger
        public WalletTransferRequest ToWalletRequest()
        {
            return new WalletTransferRequest {
                Key = request.Key,
                DebitAccountId = ServerIds.ParseLongId(request.DebitAccountId, "account"),
                CreditAccountId = ServerIds.ParseLongId(request.CreditAccountId, "account"),
                Amount = request.Amount,
                IsPending = request.IsPending,
                Code = request.Code
            };
        }
    }
}
