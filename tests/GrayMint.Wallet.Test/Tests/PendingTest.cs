using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Test.Tests;

[TestClass]
public class PendingTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod, EveryDatabase]
    public async Task PendingDebitHoldsTheMoneyAndPendingCreditIsNotThereYet(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await CreateFunded(wallet, 10);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);

        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 6, isPending: true, key: "hold"));
        account = await wallet.GetAccount(account.AccountId, CancellationToken);
        Assert.AreEqual(10m, account.Balance);
        Assert.AreEqual(4m, account.Available);
        other = await wallet.GetAccount(other.AccountId, CancellationToken);
        Assert.AreEqual(6m, other.CreditsPending);
        Assert.AreEqual(0m, other.Available);

        await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 5)));
        await Assert.ThrowsExactlyAsync<InsufficientBalanceException>(() =>
            wallet.Transfer(CancellationToken, TestWallet.Request(other, account, 1)));
    }

    [TestMethod, EveryDatabase]
    public async Task PostMakesThePendingAmountPostedAndVoidGivesItBack(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await CreateFunded(wallet, 10);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(account, other, 6, isPending: true, key: "post-me"),
            TestWallet.Request(account, other, 3, isPending: true, key: "void-me"));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        var posted = await walletService.PostPendingTransfers(TestWallet.LedgerId, ["post-me"], CancellationToken);
        Assert.AreEqual(WalletTransferType.PostPending, posted[0].Type);
        Assert.IsNull(posted[0].Key);
        var voided = await walletService.VoidPendingTransfers(TestWallet.LedgerId, ["void-me"], CancellationToken);
        Assert.AreEqual(WalletTransferType.VoidPending, voided[0].Type);

        var pending = await walletService.GetTransfer(TestWallet.LedgerId, "post-me", CancellationToken);
        Assert.AreEqual(posted[0].TransferId, (await walletService.FindSettlement(TestWallet.LedgerId, pending.TransferId, CancellationToken))?.TransferId);

        account = await wallet.GetAccount(account.AccountId, CancellationToken);
        Assert.AreEqual(0m, account.DebitsPending);
        Assert.AreEqual(6m, account.DebitsPosted);
        Assert.AreEqual(4m, account.Available);
        other = await wallet.GetAccount(other.AccountId, CancellationToken);
        Assert.AreEqual(0m, other.CreditsPending);
        Assert.AreEqual(6m, other.Available);
    }

    [TestMethod, EveryDatabase]
    public async Task SettlementSentAgainChangesNothingAndTheOtherWayIsRefused(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await CreateFunded(wallet, 10);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken,
            TestWallet.Request(account, other, 6, isPending: true, key: "p1"),
            TestWallet.Request(account, other, 2, isPending: true, key: "p2"));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        var first = await walletService.PostPendingTransfers(TestWallet.LedgerId, ["p1"], CancellationToken);
        var again = await walletService.PostPendingTransfers(TestWallet.LedgerId, ["p1"], CancellationToken);
        Assert.AreEqual(first[0].TransferId, again[0].TransferId);

        // in a list, the one settled before comes back, the other is settled now, each in its place
        var both = await walletService.PostPendingTransfers(TestWallet.LedgerId, ["p2", "p1"], CancellationToken);
        Assert.AreEqual(first[0].TransferId, both[1].TransferId);
        Assert.AreEqual(2m, both[0].Amount);
        Assert.AreEqual(8m, (await wallet.GetAccount(account.AccountId, CancellationToken)).DebitsPosted);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            walletService.VoidPendingTransfers(TestWallet.LedgerId, ["p1"], CancellationToken));
        Assert.AreEqual(8m, (await wallet.GetAccount(other.AccountId, CancellationToken)).Balance);
    }

    [TestMethod, EveryDatabase]
    public async Task OnlyAPendingTransferOfTheLedgerIsSettled(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await CreateFunded(wallet, 10);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 1, key: "posted"));
        await wallet.Transfer(CancellationToken, TestWallet.Request(account, other, 1, isPending: true, key: "voided"));

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        await walletService.VoidPendingTransfers(TestWallet.LedgerId, ["voided"], CancellationToken);

        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            walletService.PostPendingTransfers(TestWallet.LedgerId, ["posted"], CancellationToken));
        await Assert.ThrowsExactlyAsync<WalletConflictException>(() =>
            walletService.PostPendingTransfers(TestWallet.LedgerId, ["voided"], CancellationToken));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            walletService.PostPendingTransfers(TestWallet.LedgerId, ["nothing"], CancellationToken));
        await Assert.ThrowsExactlyAsync<NotExistsException>(() =>
            walletService.PostPendingTransfers(2, ["voided"], CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            walletService.PostPendingTransfers(TestWallet.LedgerId, ["voided", "voided"], CancellationToken));
    }

    // a key is unique in its ledger only, so another ledger's pending transfer of the same key is left as it is
    [TestMethod, EveryDatabase]
    public async Task PendingTransferOfTheSameKeyInAnotherLedgerIsLeftAlone(WalletDatabaseProvider provider)
    {
        await using var wallet = await TestWallet.Create(provider, CancellationToken);
        var account = await CreateFunded(wallet, 10);
        var other = await wallet.CreateAccount("other", 0, CancellationToken);
        var source2 = await wallet.CreateSource(CancellationToken, ledgerId: 2);
        var account2 = await wallet.CreateAccount("owner", 0, CancellationToken, ledgerId: 2);

        await using var dbContext = wallet.CreateDbContext();
        var walletService = TestWallet.CreateService(dbContext);
        await walletService.CreateTransfers(TestWallet.LedgerId,
            [TestWallet.Request(account, other, 3, isPending: true, key: "held")], CancellationToken);
        var heldInLedger2 = await walletService.CreateTransfers(2,
            [TestWallet.Request(source2, account2, 4, isPending: true, key: "held")], CancellationToken);

        var settlements = await walletService.PostPendingTransfers(TestWallet.LedgerId, ["held"], CancellationToken);
        Assert.AreEqual(TestWallet.LedgerId, settlements.Single().LedgerId);
        Assert.AreEqual(3m, (await wallet.GetAccount(other.AccountId, CancellationToken)).CreditsPosted);
        Assert.IsNull(await walletService.FindSettlement(2, heldInLedger2[0].TransferId, CancellationToken));
        Assert.AreEqual(4m, (await wallet.GetAccount(account2.AccountId, CancellationToken, ledgerId: 2)).CreditsPending);
    }

    private async Task<Accounts.WalletAccount> CreateFunded(TestWallet wallet, decimal amount)
    {
        var source = await wallet.CreateSource(CancellationToken);
        var account = await wallet.CreateAccount("owner", 0, CancellationToken);
        await wallet.Transfer(CancellationToken, TestWallet.Request(source, account, amount));
        return account;
    }
}
