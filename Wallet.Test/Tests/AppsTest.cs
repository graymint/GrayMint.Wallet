using EWallet.Test.Helper;
using GrayMint.Common.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using EWallet.Api;

namespace EWallet.Test.Tests;

[TestClass]
public class AppsTest : BaseControllerTest
{
    [TestMethod]
    public async Task Validate_system_wallet()
    {
        // validate min balance and system wallet creation by app
        var app = await TestInit1.AppsClient.GetAsync(TestInit1.AppId);

        // create a currency
        var walletDom = await WalletDom.Create(TestInit1);
        var systemWallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(systemWallet.Currencies);
        Assert.AreEqual(-long.MaxValue, systemWallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance);
    }

    [TestMethod]
    public async Task Success_clear_all()
    {
        // Create payment 
        var app = await TestInit1.AppsClient.GetAsync(TestInit1.AppId);

        // create wallets
        var walletDom = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, null);

        // increase balance of wallet2
       var order =  await walletDom.CreateOrder(TestInit1, transactionType: TransactionType.Sale,
            receiverWalletId: walletDom2.Wallet.WalletId, amount: 100);
        // change token
        await TestInit1.AppsClient.ClearAllAsync(app.AppId);

        try
        {
            await TestInit1.OrdersClient.GetOrderAsync(app.AppId, order.OrderId);
            Assert.Fail("Orders must be delete");
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
        }

        await TestInit1.AppsClient.GetAsync(app.AppId);
    }
}