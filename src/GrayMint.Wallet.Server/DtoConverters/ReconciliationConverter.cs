using GrayMint.Wallet.Reconciliation;
using PackageCurrencyImbalance = GrayMint.Wallet.Reconciliation.CurrencyImbalance;
using PackageWalletDiscrepancy = GrayMint.Wallet.Reconciliation.WalletDiscrepancy;
using PackageWalletTotals = GrayMint.Wallet.Reconciliation.WalletTotals;

namespace GrayMint.Wallet.Server.DtoConverters;

// Inside the namespace, so it is read before the package's, which names its classes the same.
using GrayMint.Wallet.Server.Dtos.Reconciliation;

// The API's own shapes, apart from the package's, so a change to the package never changes the API unseen. Both
// name the parts of the report the same: here the API's is the plain name, the package's the one with Package in
// front.
internal static class ReconciliationConverter
{
    extension(WalletReconciliationReport report)
    {
        public ReconciliationReport ToDto()
        {
            return new ReconciliationReport {
                IsReconciled = report.IsReconciled,
                WalletCount = report.WalletCount,
                WalletDiscrepancies = report.WalletDiscrepancies.Select(x => x.ToDto()).ToArray(),
                CurrencyImbalances = report.CurrencyImbalances.Select(x => x.ToDto()).ToArray()
            };
        }
    }

    extension(PackageWalletDiscrepancy discrepancy)
    {
        public WalletDiscrepancy ToDto()
        {
            return new WalletDiscrepancy {
                Wallet = discrepancy.Wallet.ToDto(),
                FromTransfers = discrepancy.FromTransfers.ToDto(),
                LastTransaction = discrepancy.LastTransaction?.ToDto()
            };
        }
    }

    extension(PackageCurrencyImbalance imbalance)
    {
        public CurrencyImbalance ToDto()
        {
            return new CurrencyImbalance {
                TenantId = imbalance.TenantId,
                Currency = imbalance.Currency,
                Totals = imbalance.Totals.ToDto()
            };
        }
    }

    extension(PackageWalletTotals totals)
    {
        public WalletTotals ToDto()
        {
            return new WalletTotals {
                PostedIn = totals.PostedIn,
                PostedOut = totals.PostedOut,
                PendingIn = totals.PendingIn,
                PendingOut = totals.PendingOut
            };
        }
    }
}
