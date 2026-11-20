using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;
using GrayMint.Wallet.Wallets;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Test.Tests;

// Each call in a scope of its own, as requests of a host come in.
[TestClass]
public class ConcurrencyTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task PaymentsAtOnceNeverSpendMoreThanTheWalletHas(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var other = await testWallet.CreateWallet("other", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 10));

        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => TryTransfer(testWallet,
            TestWallet.Request(wallet, other, 1))));

        Assert.AreEqual(10, results.Count(x => x));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(10m, (await testWallet.GetWallet(other.WalletId, CancellationToken)).PostedBalance);
        await using var scope = testWallet.CreateScope();
        Assert.IsTrue((await scope.Reconciliation.Reconcile(CancellationToken)).IsReconciled);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(wallet, CancellationToken), 11);
    }

    // the wallets are locked in id order, whichever side each is on
    [TestMethod, EveryDatabase]
    public async Task TransfersBothWaysAtOnceNeverWaitForEachOther(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var first = await testWallet.CreateWallet("first", 0, CancellationToken);
        var second = await testWallet.CreateWallet("second", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken,
            TestWallet.Request(source, first, 100),
            TestWallet.Request(source, second, 100));

        var requests = Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? TestWallet.Request(first, second, 1) : TestWallet.Request(second, first, 1))
            .ToArray();
        await Task.WhenAll(requests.Select(request => testWallet.Transfer(CancellationToken, request)));

        Assert.AreEqual(100m, (await testWallet.GetWallet(first.WalletId, CancellationToken)).PostedBalance);
        Assert.AreEqual(100m, (await testWallet.GetWallet(second.WalletId, CancellationToken)).PostedBalance);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(first, CancellationToken), 41);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(second, CancellationToken), 41);
    }

    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersSentTwiceAtOnceAreMadeOnce(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        var linked = new[] { TestWallet.Request(source, wallet, 3, externalReferenceKey: "once") };

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => testWallet.Transfer(CancellationToken, linked)));

        Assert.AreEqual(1, results.Select(x => x[0].TransferId).Distinct().Count());
        Assert.AreEqual(3m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedBalance);
    }

    // A post sent twice at once waits at the lock for the first, then finds its settlement: both get that one, and
    // neither hears of a conflict.
    [TestMethod, EveryDatabase]
    public async Task SettlementSentTwiceAtOnceIsMadeOnce(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 3, isPending: true, externalReferenceKey: "held"));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => {
            await using var scope = testWallet.CreateScope();
            return await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], CancellationToken);
        }));

        Assert.AreEqual(1, results.Select(x => x[0].TransferId).Distinct().Count());
        Assert.AreEqual(3m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).PostedIn);
    }

    // A limit raised while a payment holds the wallet waits for it, then sees what the payment left.
    [TestMethod, EveryDatabase]
    public async Task MinAvailableBalanceRaisedWhileAPaymentHoldsTheWalletWaitsForIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await TestWallet.Create(provider, CancellationToken);
        var source = await testWallet.CreateSource(CancellationToken);
        var wallet = await testWallet.CreateWallet("owner", 0, CancellationToken);
        await testWallet.Transfer(CancellationToken, TestWallet.Request(source, wallet, 10));

        // a payment of 6 holds the wallet, as a write does, and has not committed yet
        await using var holder = testWallet.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(CancellationToken);
        await TestWallet.LockWallet(holder, wallet, CancellationToken);

        await using var scope = testWallet.CreateScope();
        var raise = scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(5), CancellationToken);
        await testWallet.WaitForBlockedCalls(1, CancellationToken);
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedOut\" = 6 WHERE \"WalletId\" = {wallet.WalletId}",
            CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => raise);
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId, CancellationToken)).MinAvailableBalance);
    }

    // Each transaction's balance is the one before it with its amount: the lock lets one call at a time write a
    // wallet, and its transfers take their ids in that order.
    private static void AssertTransactionsFollowOneAnother(IReadOnlyList<WalletTransaction> transactions,
        int count)
    {
        Assert.HasCount(count, transactions);
        var balance = 0m;
        foreach (var transaction in transactions) {
            balance += transaction.Amount;
            Assert.AreEqual(balance, transaction.PostedBalance);
        }
    }

    private async Task<bool> TryTransfer(TestWallet testWallet, WalletTransferCreateRequest request)
    {
        try {
            await testWallet.Transfer(CancellationToken, request);
            return true;
        }
        catch (InsufficientFundsException) {
            return false;
        }
    }
}
