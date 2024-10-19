using EWallet.Api;
using EWallet.Test.Helper;
using GrayMint.Common.ApiClients;
using GrayMint.Common.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class WalletTransactionTest : BaseControllerTest
{
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
        var orders = await TestInit1.WalletsClient.GetWalletTransactionsAsync(TestInit1.AppId,
            walletDom.Wallet.WalletId, walletDom2.Wallet.WalletId);

        Assert.AreEqual(orders.Count, 1);
        Assert.IsNotNull(orders.SingleOrDefault(x => x.SenderWalletId == walletDom.Wallet.WalletId && x.ReceiverWalletId == walletDom2.Wallet.WalletId));
        Assert.IsNull(orders.SingleOrDefault(x => x.SenderWalletId == walletDom3.Wallet.WalletId && x.ReceiverWalletId == walletDom4.Wallet.WalletId));
    }

    [TestMethod]
    public async Task Fail_Get_Transactions_Of_Specified_Wallets_With_Invalid_WalletIds()
    {
        try
        {
            await TestInit1.WalletsClient.GetWalletTransactionsAsync(TestInit1.AppId, -1);
            Assert.Fail("Invalid string format expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException),ex.ExceptionTypeName);
        }
    }
}