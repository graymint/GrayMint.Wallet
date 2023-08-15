#nullable enable
namespace EWallet.Models;

public class WalletModel
{
    public int WalletId { get; set; }
    public int AppId { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual AppModel? App { get; set; }
    public virtual ICollection<WalletTransactionModel>? OrderTransactions { get; set; }
    public virtual ICollection<WalletBalanceModel>? WalletBalances { get; set; }
}