namespace EWallet.Models;

public class WalletTransactionModel
{
    public long WalletTransactionId { get; set; }
    public long OrderItemId { get; set; }
    public int WalletId { get; set; }
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public int ReceiverWalletId { get; set; }
    public long? WalletTransactionReferenceId { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual WalletTransactionModel? ReferenceWalletTransaction { get; set; }
    public virtual WalletModel? Wallet { get; set; }
    public virtual WalletModel? ReceiverWallet { get; set; }
    public virtual OrderItemModel? OrderItem { get; set; }
}