using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Persistence.Models;

namespace GrayMint.Wallet.Transfers;

// What one call does to a locked wallet, transfer by transfer, so each of the linked transfers sees the ones before
// it.
internal sealed class WalletChange(WalletModel wallet)
{
    public WalletModel Wallet { get; } = wallet;
    public decimal PostedIn { get; private set; }
    public decimal PostedOut { get; private set; }
    public decimal PendingIn { get; private set; }
    public decimal PendingOut { get; private set; }

    public bool IsChanged => PendingOut != 0 || PostedOut != 0 || PendingIn != 0 || PostedIn != 0;

    public decimal PostedBalance => Wallet.PostedBalance + PostedIn - PostedOut;
    public decimal AvailableBalance => Wallet.AvailableBalance + PostedIn - PostedOut - PendingOut;

    // Money pending out is held at once; money pending in adds nothing to spend until it is posted.
    public void MoveOut(decimal amount, bool isPending)
    {
        if (Wallet.MinAvailableBalance != null && AvailableBalance - amount < Wallet.MinAvailableBalance)
            throw new InsufficientFundsException(WalletIds.Format(Wallet.WalletId), AvailableBalance, amount,
                Wallet.MinAvailableBalance.Value);

        if (isPending)
            PendingOut += amount;
        else
            PostedOut += amount;
    }

    public void MoveIn(decimal amount, bool isPending)
    {
        if (isPending)
            PendingIn += amount;
        else
            PostedIn += amount;
    }

    // what was held stays held until here, so settling never needs more than the wallet has
    public void SettleOut(decimal amount, bool isPosted)
    {
        PendingOut -= amount;
        if (isPosted)
            PostedOut += amount;
    }

    public void SettleIn(decimal amount, bool isPosted)
    {
        PendingIn -= amount;
        if (isPosted)
            PostedIn += amount;
    }
}
