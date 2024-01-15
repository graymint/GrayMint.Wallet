namespace EWallet.Models;

public class AppModel 
{
    public int AppId { get; set; }
    public int? SystemWalletId { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual WalletModel? SystemWallet { get; set; }
    public virtual OrderModel? Orders { get; set; }
}