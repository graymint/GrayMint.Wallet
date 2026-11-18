using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Exceptions;

namespace GrayMint.Wallet.Transfers;

// What one call does to a locked account, transfer by transfer, so each transfer of a chain sees the ones before it.
internal sealed class AccountChange(WalletAccount account)
{
    public WalletAccount Account { get; } = account;
    public decimal DebitsPending { get; private set; }
    public decimal DebitsPosted { get; private set; }
    public decimal CreditsPending { get; private set; }
    public decimal CreditsPosted { get; private set; }

    public bool IsChanged => DebitsPending != 0 || DebitsPosted != 0 || CreditsPending != 0 || CreditsPosted != 0;

    public decimal Available => Account.Available + CreditsPosted - DebitsPosted - DebitsPending;

    // A pending debit holds the amount at once; a pending credit adds nothing to spend until it is posted.
    public void Debit(decimal amount, bool isPending)
    {
        if (Account.MinBalance != null && Available - amount < Account.MinBalance)
            throw new InsufficientBalanceException(Account.AccountId, Available, amount, Account.MinBalance.Value);

        if (isPending)
            DebitsPending += amount;
        else
            DebitsPosted += amount;
    }

    public void Credit(decimal amount, bool isPending)
    {
        if (isPending)
            CreditsPending += amount;
        else
            CreditsPosted += amount;
    }

    // what was held stays held until here, so settling never needs more than the account has
    public void SettleDebit(decimal amount, bool isPosted)
    {
        DebitsPending -= amount;
        if (isPosted)
            DebitsPosted += amount;
    }

    public void SettleCredit(decimal amount, bool isPosted)
    {
        CreditsPending -= amount;
        if (isPosted)
            CreditsPosted += amount;
    }
}
