using GrayMint.Wallet.Server;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Test.Helper;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class ReconciliationTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task TotalsAddUpToTheTransfers(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", -20, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 50),
            TestWallet.Request(account, other, 7, isPending: true, key: "posted"),
            TestWallet.Request(account, other, 3, isPending: true, key: "voided"),
            TestWallet.Request(account, other, 2, isPending: true, key: "held"),
            TestWallet.Request(other, account, 15));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        await walletService.PostPendingTransfers(TestWallet.LedgerId, ["posted"], CancellationToken);
        await walletService.VoidPendingTransfers(TestWallet.LedgerId, ["voided"], CancellationToken);

        Assert.IsEmpty(await walletService.FindTotalsMismatches(CancellationToken));
        account = await wallet.GetAccount(account.AccountId, CancellationToken);
        Assert.AreEqual(58m, account.Balance);
        Assert.AreEqual(2m, account.DebitsPending);
    }

    // The search looks again at each account it finds, under the account's lock: an account put right between
    // the two looks, as one that a write was halfway through when the first one read it, is not reported.
    [TestMethod, EveryDatabase]
    public async Task AccountPutRightMeanwhileIsNotReported(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 10));
        await wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"CreditsPosted\" = 11 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken);

        // another call holds the account's lock, as a write does
        await using var holder = wallet.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(CancellationToken);
        await holder.Accounts
            .FromSqlRaw(WalletDialect.For(holder.Database).LockAccountSql, TestWallet.LedgerId, account.AccountId)
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        await using var dbContext = wallet.CreateDbContext();
        var search = TestWallet.CreateService(dbContext).FindTotalsMismatches(CancellationToken);
        await wallet.WaitForBlockedCalls(1, CancellationToken);
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Accounts\" SET \"CreditsPosted\" = 10 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        Assert.IsEmpty(await search);
    }

    [TestMethod, EveryDatabase]
    public async Task TotalChangedBehindTheServiceIsFound(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(source, account, 10),
            TestWallet.Request(source, account, 4, isPending: true));
        await wallet.Execute(
            $"UPDATE gmwallet.\"Accounts\" SET \"CreditsPosted\" = 11 WHERE \"AccountId\" = {account.AccountId}",
            CancellationToken);

        await using var dbContext = wallet.CreateDbContext();
        var mismatches = await TestWallet.CreateService(dbContext).FindTotalsMismatches(CancellationToken);
        Assert.HasCount(1, mismatches);
        Assert.AreEqual(account.AccountId, mismatches[0].AccountId);
        Assert.AreEqual(11m, mismatches[0].Stored.CreditsPosted);
        Assert.AreEqual(10m, mismatches[0].FromTransfers.CreditsPosted);
        Assert.AreEqual(4m, mismatches[0].FromTransfers.CreditsPending);
    }

    // Nothing in the database stops a transfer from being changed or deleted behind the service's back
    // (docs/design.md): this search is what finds one, as its accounts' totals no longer add up.
    [TestMethod, EveryDatabase]
    public async Task TransferChangedOrDeletedBehindTheServiceIsFound(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var changed = await wallet.CreateAccount("changed", 0, CancellationToken);
        var deleted = await wallet.CreateAccount("deleted", 0, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(source, changed, 10),
            TestWallet.Request(source, deleted, 5));
        await wallet.Execute(
            $"UPDATE gmwallet.\"Transfers\" SET \"Amount\" = 1 WHERE \"CreditAccountId\" = {changed.AccountId}",
            CancellationToken);
        await wallet.Execute(
            $"DELETE FROM gmwallet.\"Transfers\" WHERE \"CreditAccountId\" = {deleted.AccountId}",
            CancellationToken);

        await using var dbContext = wallet.CreateDbContext();
        var mismatches = await TestWallet.CreateService(dbContext).FindTotalsMismatches(CancellationToken);
        CollectionAssert.AreEquivalent(new[] { source.AccountId, changed.AccountId, deleted.AccountId },
            mismatches.Select(x => x.AccountId).ToArray());
        Assert.AreEqual(1m, mismatches.Single(x => x.AccountId == changed.AccountId).FromTransfers.CreditsPosted);
        Assert.AreEqual(0m, mismatches.Single(x => x.AccountId == deleted.AccountId).FromTransfers.CreditsPosted);
    }
}
