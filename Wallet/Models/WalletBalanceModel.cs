namespace EWallet.Models;

public class WalletBalanceModel
{
    public int WalletBalanceId { get; set; }
    public decimal Balance { get; set; }
    public decimal MinBalance { get; set; }
    public int WalletId { get; set; }
    public int CurrencyId { get; set; }
    public DateTime ModifiedTime { get; set; }
    public virtual WalletModel? Wallet { get; set; }
    public virtual CurrencyModel? Currency { get; set; }
}