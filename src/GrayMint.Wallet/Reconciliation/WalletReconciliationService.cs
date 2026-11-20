using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Persistence.Models;
using GrayMint.Wallet.Persistence.Views;

namespace GrayMint.Wallet.Reconciliation;

/// <summary>
/// Reconciles the books: every wallet against its transfers and its wallet transactions, and every currency of a
/// tenant against zero.
/// </summary>
public sealed class WalletReconciliationService
{
    private readonly WalletRepo _repo;

    // made by the registration alone (WalletExtension.AddGrayMintWallet)
    internal WalletReconciliationService(WalletRepo repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Compares the books and reports where they disagree, changing nothing: every wallet whose totals are not what
    /// its transfers add up to, or whose balances are not the ones its newest wallet transaction left, and every
    /// currency of a tenant whose wallets don't add up to zero. Each is a write that went round the wallet, or a fault
    /// in it. A wallet found is looked at again under its lock, so a call still writing it is never taken for one.
    /// Nothing calls it on its own: it reads every wallet and transfer the caller can see, and in a SQL Server database
    /// that reads committed rows by locks holds off writes to the wallets while it adds the currencies up, so the host
    /// calls it when it wants the report.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Where the books disagree, which is nowhere on reconciled books.</returns>
    public async Task<WalletReconciliationReport> Reconcile(CancellationToken cancellationToken)
    {
        // The currencies first, while the call holds no wallet's lock. In a host's database transaction the wallets
        // looked at again stay locked until it ends, and on SQL Server a write waiting for one of them would hold
        // off the table's shared lock, which waits for that write.
        var currencies = await GetCurrencyTotals(cancellationToken);
        var walletDiscrepancies = new List<WalletDiscrepancy>();
        foreach (var candidate in await FindCandidates(cancellationToken)) {
            var walletDiscrepancy = await LookAgain(candidate.Wallet, cancellationToken);
            if (walletDiscrepancy != null)
                walletDiscrepancies.Add(walletDiscrepancy);
        }

        return new WalletReconciliationReport {
            WalletCount = currencies.Sum(x => x.WalletCount),
            WalletDiscrepancies = walletDiscrepancies,
            CurrencyImbalances = currencies
                .Where(x => x.Totals.PostedIn != x.Totals.PostedOut || x.Totals.PendingIn != x.Totals.PendingOut)
                .Select(x => x.ToDto())
                .ToArray()
        };
    }

    private async Task<IReadOnlyList<CurrencyTotalsView>> GetCurrencyTotals(CancellationToken cancellationToken)
    {
        await using var session = await _repo.BeginRead(cancellationToken);
        return await _repo.GetCurrencyTotals(cancellationToken);
    }

    // One query isn't sure to read a wallet's totals, its transfers and its wallet transactions at one moment (SQL
    // Server, unless the database reads committed rows by their versions), so it only names the wallets to look at
    // again.
    private async Task<IReadOnlyList<WalletReconciliationView>> FindCandidates(CancellationToken cancellationToken)
    {
        await using var session = await _repo.BeginRead(cancellationToken);
        return await _repo.GetWalletDiscrepancies(walletId: null, cancellationToken);
    }

    // under the wallet's lock, which no write of it gets past
    private async Task<WalletDiscrepancy?> LookAgain(WalletModel wallet, CancellationToken cancellationToken)
    {
        await using var session = await _repo.BeginWrite(cancellationToken);
        await _repo.LockWallets(wallet.TenantId, [wallet.WalletId], cancellationToken);
        var discrepancy = (await _repo.GetWalletDiscrepancies(wallet.WalletId, cancellationToken)).SingleOrDefault();
        if (discrepancy == null)
            return null;

        var lastTransaction = await _repo.GetWalletTransactions(wallet.TenantId, wallet.WalletId,
            beforeTransferId: null, limit: 1, cancellationToken);
        return discrepancy.ToDto(lastTransaction.SingleOrDefault());
    }
}
