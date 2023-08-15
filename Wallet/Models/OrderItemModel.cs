namespace EWallet.Models;

public class OrderItemModel
{
    public long OrderItemId { get; set; }
    public long OrderId { get; set; }
    public int SenderWalletId { get; set; }
    public int ReceiverWalletId { get; set; }
    public decimal Amount { get; set; }
    
    public virtual OrderModel? Order { get; set; }
    public virtual ICollection<WalletTransactionModel>? OrderTransactions { get; set; }
}