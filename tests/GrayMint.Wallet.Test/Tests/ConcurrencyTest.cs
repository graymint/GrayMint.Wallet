using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

// Each call on a context of its own, as requests of a host come in.
[TestClass]
public class ConcurrencyTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task PaymentsAtOnceNeverSpendMoreThanTheAccountHas(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, 10));

        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => TryTransfer(wallet,
            TestWallet.Request(account, other, 1))));

        Assert.AreEqual(10, results.Count(x => x));
        Assert.AreEqual(0m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(10m, (await wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
        await using var dbContext = wallet.CreateDbContext();
        Assert.IsEmpty(await TestWallet.CreateService(dbContext).FindTotalsMismatches(CancellationToken));
    }

    // the accounts are locked in id order, whichever side each is on
    [TestMethod, EveryDatabase]
    public async Task TransfersBothWaysAtOnceNeverWaitForEachOther(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var first = await wallet.CreateAccount("first", 0, CancellationToken);
        var second = await wallet.CreateAccount("second", 0, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(source, first, 100),
            TestWallet.Request(source, second, 100));

        var requests = Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? TestWallet.Request(first, second, 1) : TestWallet.Request(second, first, 1))
            .ToArray();
        await Task.WhenAll(requests.Select(request => wallet.Transfer(CancellationToken, request)));

        Assert.AreEqual(100m, (await wallet.GetAccount(first.AccountId, CancellationToken)).Balance);
        Assert.AreEqual(100m, (await wallet.GetAccount(second.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task ChainSentTwiceAtOnceIsMadeOnce(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        var chain = new[] { TestWallet.Request(source, account, 3, key: "once") };

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => wallet.Transfer(CancellationToken, chain)));

        Assert.AreEqual(1, results.Select(x => x[0].TransferId).Distinct().Count());
        Assert.AreEqual(3m, (await wallet.GetAccount(account.AccountId, CancellationToken)).Balance);
    }

    private async Task<bool> TryTransfer(TestWallet wallet, WalletTransferRequest request)
    {
        try {
            await wallet.Transfer(CancellationToken, request);
            return true;
        }
        catch (InsufficientBalanceException) {
            return false;
        }
    }
}
