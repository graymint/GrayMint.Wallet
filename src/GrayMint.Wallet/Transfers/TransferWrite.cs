using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Transfers;

// A transfer a call makes, with the balances it leaves on its two wallets, taken when it is applied: each of the
// linked transfers leaves its own. Its wallet transactions are made once the transfer is written, which gives it its
// id.
internal sealed class TransferWrite(TransferModel transfer, WalletChange fromChange, WalletChange toChange)
{
    private readonly decimal _fromPostedBalance = fromChange.PostedBalance;
    private readonly decimal _fromAvailableBalance = fromChange.AvailableBalance;
    private readonly decimal _toPostedBalance = toChange.PostedBalance;
    private readonly decimal _toAvailableBalance = toChange.AvailableBalance;

    public TransferModel Transfer { get; } = transfer;

    public IReadOnlyList<WalletTransactionModel> CreateWalletTransactions()
    {
        if (Transfer.TransferId == 0)
            throw new InvalidOperationException("A transfer has no wallet transactions before it is written.");

        // a void undoes its pending transfer: what was held goes back, and what was announced is taken back
        var outAmount = Transfer.Type == WalletTransferType.VoidPending ? Transfer.Amount : -Transfer.Amount;

        return [
            new WalletTransactionModel {
                TenantId = Transfer.TenantId,
                WalletId = Transfer.FromWalletId,
                TransferId = Transfer.TransferId,
                Amount = outAmount,
                PostedBalance = _fromPostedBalance,
                AvailableBalance = _fromAvailableBalance
            },
            new WalletTransactionModel {
                TenantId = Transfer.TenantId,
                WalletId = Transfer.ToWalletId,
                TransferId = Transfer.TransferId,
                Amount = -outAmount,
                PostedBalance = _toPostedBalance,
                AvailableBalance = _toAvailableBalance
            }
        ];
    }
}
