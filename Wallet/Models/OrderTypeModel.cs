namespace EWallet.Models;

public class OrderTypeModel
{
    public int OrderTypeId { get; set; }
    public int AppId { get; set; }
    public required string OrderTypeName { get; set; }
    public virtual ICollection<OrderModel>? Orders { get; set; }
    public virtual AppModel? App { get; set; }
}