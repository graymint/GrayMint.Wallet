using GrayMint.Wallet.Persistence.Views;
using GrayMint.Wallet.Wallets;

namespace GrayMint.Wallet.Reconciliation;

internal static class WalletReconciliationConverter
{
    extension(WalletReconciliationView view)
    {
        public WalletDiscrepancy ToDto(WalletTransactionView? lastTransaction)
        {
            return new WalletDiscrepancy {
                Wallet = view.Wallet.ToDto(),
                FromTransfers = view.FromTransfers,
                LastTransaction = lastTransaction?.ToDto()
            };
        }
    }

    extension(CurrencyTotalsView view)
    {
        public CurrencyImbalance ToDto()
        {
            return new CurrencyImbalance {
                TenantId = WalletIds.Format(view.TenantId),
                Currency = view.Currency,
                Totals = view.Totals
            };
        }
    }
}
