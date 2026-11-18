using GrayMint.Wallet.Accounts;

namespace GrayMint.Wallet.Server.Accounts;

public static class AccountConverter
{
    extension(WalletAccount account)
    {
        public Account ToDto()
        {
            return new Account {
                AccountId = ServerIds.Format(account.AccountId),
                LedgerId = ServerIds.Format(account.LedgerId),
                OwnerId = account.OwnerId,
                Code = account.Code,
                Currency = account.Currency,
                MinBalance = account.MinBalance,
                DebitsPending = account.DebitsPending,
                DebitsPosted = account.DebitsPosted,
                CreditsPending = account.CreditsPending,
                CreditsPosted = account.CreditsPosted,
                Balance = account.Balance,
                Available = account.Available,
                CreatedTime = account.CreatedTime
            };
        }
    }

    extension(AccountCreateRequest request)
    {
        public WalletAccountCreateRequest ToWalletRequest()
        {
            return new WalletAccountCreateRequest {
                OwnerId = request.OwnerId,
                Code = request.Code,
                Currency = request.Currency,
                MinBalance = request.MinBalance
            };
        }
    }
}
