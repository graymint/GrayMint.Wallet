namespace EWallet.Models;

public class AppModel
{
    public required int AppId { get; init; }
    public required int? SystemWalletId { get; set; }
    public required DateTime CreatedTime { get; init; }

    public virtual WalletModel? SystemWallet { get; set; }
}