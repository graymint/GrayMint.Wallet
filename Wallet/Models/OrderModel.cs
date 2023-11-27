namespace EWallet.Models;
public class OrderModel
{
    public long OrderId { get; set; }
    public Guid OrderReferenceNumber { get; set; }
    public int AppId { get; set; }
    public int CurrencyId { get; set; }
    public TransactionType TransactionType { get; set; }
    public int OrderTypeId { get; set; }
    public DateTime? AuthorizedTime { get; set; }
    public DateTime? CapturedTime { get; set; }
    public DateTime? VoidedTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime ModifiedTime { get; set; }
    public DateTime? ProcessTime { get; set; }

    public virtual AppModel? App { get; set; }
    public virtual CurrencyModel? Currency { get; set; }
    public virtual TransactionTypeLookup? TransactionTypeLookup { get; set; }
    public virtual ICollection<OrderItemModel>? OrderItems { get; set; }
 
}
