#nullable enable
using EWallet.Repo;

namespace EWallet.Validations;

public class OrderValidations
{
    private readonly WalletRepo _walletRepo;

    public OrderValidations(WalletRepo walletRepo)
    {
        _walletRepo = walletRepo;
    }


}