#nullable enable
namespace EWallet.Models;

public class CurrencyModel
{
    public int CurrencyId { get; set; }
    public int AppId { get; set; }

    public virtual AppModel? App { get; set; }
    public virtual ICollection<WalletBalanceModel>? WalletBalances { get; set; }
    public virtual ICollection<OrderModel>? Orders { get; set; }


}