using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Persistence.Views;

// A wallet transaction with its transfer, as a wallet's statement shows it.
internal sealed class WalletTransactionView
{
    public required WalletTransactionModel WalletTransaction { get; init; }
    public required TransferModel Transfer { get; init; }
}
