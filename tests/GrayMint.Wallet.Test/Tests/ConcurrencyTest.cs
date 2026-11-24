using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;
using GrayMint.Wallet.Wallets;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Test.Tests;

// Each call in a scope of its own, as requests of a host come in.
[TestClass]
public class ConcurrencyTest : TestBase
{
    [TestMethod, EveryDatabase]
    public async Task PaymentsAtOnceNeverSpendMoreThanTheWalletHas(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var other = await testWallet.CreateWallet("other", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10));

        // Task.WhenAll awaits every call before testWallet is disposed
        // ReSharper disable once AccessToDisposedClosure
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => TryTransfer(testWallet,
            TestWallet.Request(wallet, other, 1))));

        Assert.AreEqual(10, results.Count(x => x));
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId)).PostedBalance);
        Assert.AreEqual(10m, (await testWallet.GetWallet(other.WalletId)).PostedBalance);
        await using var scope = testWallet.CreateScope();
        Assert.IsTrue((await scope.Reconciliation.Reconcile(TestCt)).IsReconciled);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(wallet), 11);
    }

    // the wallets are locked in id order, whichever side each is on
    [TestMethod, EveryDatabase]
    public async Task TransfersBothWaysAtOnceNeverWaitForEachOther(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var first = await testWallet.CreateWallet("first", 0);
        var second = await testWallet.CreateWallet("second", 0);
        await testWallet.Transfer(TestWallet.Request(source, first, 100),
            TestWallet.Request(source, second, 100));

        var requests = Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? TestWallet.Request(first, second, 1) : TestWallet.Request(second, first, 1))
            .ToArray();

        // Task.WhenAll awaits every call before testWallet is disposed
        // ReSharper disable once AccessToDisposedClosure
        await Task.WhenAll(requests.Select(request => testWallet.Transfer(request)));

        Assert.AreEqual(100m, (await testWallet.GetWallet(first.WalletId)).PostedBalance);
        Assert.AreEqual(100m, (await testWallet.GetWallet(second.WalletId)).PostedBalance);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(first), 41);
        AssertTransactionsFollowOneAnother(await testWallet.GetTransactions(second), 41);
    }

    [TestMethod, EveryDatabase]
    public async Task LinkedTransfersSentTwiceAtOnceAreMadeOnce(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        var linked = new[] { TestWallet.Request(source, wallet, 3, externalReferenceKey: "once") };

        // Task.WhenAll awaits every call before testWallet is disposed
        // ReSharper disable once AccessToDisposedClosure
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => testWallet.Transfer(linked)));

        Assert.HasCount(1, results.Select(x => x[0].TransferId).Distinct());
        Assert.AreEqual(3m, (await testWallet.GetWallet(wallet.WalletId)).PostedBalance);
    }

    // A post sent twice at once waits at the lock for the first, then finds its settlement: both get that one, and
    // neither hears of a conflict.
    [TestMethod, EveryDatabase]
    public async Task SettlementSentTwiceAtOnceIsMadeOnce(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 3, isPending: true, externalReferenceKey: "held"));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => {
            // Task.WhenAll awaits every call before testWallet is disposed
            // ReSharper disable once AccessToDisposedClosure
            await using var scope = testWallet.CreateScope();
            return await scope.Transfers.PostPending(TestWallet.TenantId, ["held"], TestCt);
        }));

        Assert.HasCount(1, results.Select(x => x[0].TransferId).Distinct());
        Assert.AreEqual(3m, (await testWallet.GetWallet(wallet.WalletId)).PostedIn);
    }

    // A limit raised while a payment holds the wallet waits for it, then sees what the payment left.
    [TestMethod, EveryDatabase]
    public async Task MinAvailableBalanceRaisedWhileAPaymentHoldsTheWalletWaitsForIt(WalletDatabaseProvider provider)
    {
        await using var testWallet = await CreateTestWallet(provider);
        var source = await testWallet.CreateSource();
        var wallet = await testWallet.CreateWallet("owner", 0);
        await testWallet.Transfer(TestWallet.Request(source, wallet, 10));

        // a payment of 6 holds the wallet, as a write does, and has not committed yet
        await using var holder = testWallet.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await TestWallet.LockWallet(holder, wallet);

        await using var scope = testWallet.CreateScope();
        var raise = scope.Wallets.Update(TestWallet.TenantId, wallet.WalletId, TestWallet.LimitUpdate(5), TestCt);
        await testWallet.WaitForBlockedCalls(1);
        await holder.Database.ExecuteSqlRawAsync(
            $"UPDATE gmwallet.\"Wallets\" SET \"PostedOut\" = 6 WHERE \"WalletId\" = {wallet.WalletId}");

        await transaction.CommitAsync();

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() => raise);
        Assert.AreEqual(0m, (await testWallet.GetWallet(wallet.WalletId)).MinAvailableBalance);
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
            await testWallet.Transfer(request);
            return true;
        }
        catch (InsufficientFundsException) {
            return false;
        }
    }
}
