using EWallet.Api;
using EWallet.Test.Helper;
using GrayMint.Common.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class WalletTest : BaseControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        // Act
        var walletCreated = await TestInit1.WalletsClient.CreateWalletAsync(TestInit1.AppId);

        // Get Wallet
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletCreated.WalletId);

        // Assert
        Assert.AreEqual(walletCreated.WalletId, wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(0, wallet.Currencies.Count);
    }

    [TestMethod]
    public async Task Fail_GetWallet_With_AppId_That_Does_Not_Exists()
    {
        try
        {
            // Act
            await TestInit1.WalletsClient.GetWalletAsync(0, 1);
            Assert.Fail("Sequence contains no element error expected");
        }
        catch (ApiException ex) when (ex.Message.Contains("Sequence contains no element"))
        {
        }
    }

    [TestMethod]
    public async Task Fail_GetWallet_With_WalletId_That_Does_Not_Exists_For_App()
    {
        try
        {
            // Act
            await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, 0);
            Assert.Fail("Sequence contains no element error expected");
        }
        catch (ApiException ex) when (ex.Message.Contains("Sequence contains no element"))
        {
        }
    }

    [TestMethod]
    public async Task Success_Get_Transactions_Of_Specified_Wallets()
    {
        // create wallet1
        var walletDom = await WalletDom.Create(TestInit1);

        // create wallet2
        var walletDom2 = await WalletDom.Create(TestInit1, null);

        // create wallet3
        var walletDom3 = await WalletDom.Create(TestInit1);

        // create wallet4
        var walletDom4 = await WalletDom.Create(TestInit1, null);

        // increase balance of wallet2
        await walletDom.CreateOrder(TestInit1, transactionType: TransactionType.Sale,
            receiverWalletId: walletDom2.Wallet.WalletId, amount: 100);

        // create Sale order between created wallets
        await walletDom3.CreateOrder(TestInit1, transactionType: TransactionType.Sale,
            receiverWalletId: walletDom4.Wallet.WalletId, amount: 100);

        var orders = await TestInit1.WalletsClient.GetWalletTransactionsOfParticipantsAsync(TestInit1.AppId,
            $"{walletDom.Wallet.WalletId},{walletDom2.Wallet.WalletId}");

        Assert.AreEqual(orders.Count, 1);
        Assert.IsNotNull(orders.SingleOrDefault(x => x.SenderWalletId == walletDom.Wallet.WalletId && x.ReceiverWalletId == walletDom2.Wallet.WalletId));
        Assert.IsNull(orders.SingleOrDefault(x => x.SenderWalletId == walletDom3.Wallet.WalletId && x.ReceiverWalletId == walletDom4.Wallet.WalletId));
    }

    [TestMethod]
    public async Task Fail_Get_Transactions_Of_Specified_Wallets_With_Invalid_WalletIds()
    {
        try
        {
            await TestInit1.WalletsClient.GetWalletTransactionsOfParticipantsAsync(TestInit1.AppId, Guid.NewGuid().ToString());
            Assert.Fail("Invalid string format expected");
        }
        catch (ApiException ex)
        {
            Assert.IsTrue(ex.Message.Contains("was not in a correct format"));
        }
    }

    [TestMethod]
    public async Task Success_MinBalance_when_does_not_set_yet()
    {
        // create dom
        var walletDom = await WalletDom.Create(TestInit1, null);

        // set min balance
        const int minBalance = -1000;
        var request = new SetMinBalanceRequest
        {
            CurrencyId = walletDom.CurrencyId,
            MinBalance = minBalance
        };
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, walletDom.Wallet.WalletId, request);

        // Assert
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, minBalance);
    }

    [TestMethod]
    public async Task Success_MinBalance_when_is_set_before()
    {
        // create dom
        var walletDom = await WalletDom.Create(TestInit1);

        // check min balance initialized
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);

        // Assert
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, -long.MaxValue);

        // set min balance
        const int minBalance = -1000;
        var request = new SetMinBalanceRequest
        {
            CurrencyId = walletDom.CurrencyId,
            MinBalance = minBalance
        };
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, walletDom.Wallet.WalletId, request);

        // Assert
        wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, minBalance);
    }
}