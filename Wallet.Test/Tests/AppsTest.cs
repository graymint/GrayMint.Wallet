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

        // create authorize order
        // Create wallets
        var systemWalletDom = await WalletDom.Create(TestInit1);
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1);
        var walletDom3 = await WalletDom.Create(TestInit1);
        var walletDom4 = await WalletDom.Create(TestInit1);
        var walletDom5 = await WalletDom.Create(TestInit1);
        var walletDom6 = await WalletDom.Create(TestInit1);

        // Transfer to wallets in order to make available balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 100, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 150, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 20, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom5.Wallet.WalletId, amount: 40, transactionType: TransactionType.Sale);

        // Create order request
        var orderTypeId = new Random().Next(int.MinValue, int.MaxValue);
        var request = new CreateOrderRequest
        {
            OrderId = Guid.NewGuid(),
            CurrencyId = systemWalletDom.CurrencyId,
            OrderTypeId = orderTypeId,
            TransactionType = TransactionType.Authorize,
            ParticipantWallets = new List<ParticipantTransferItem>
                {
                    new ()
                    {
                        SenderWalletId = walletDom1.Wallet.WalletId,
                        ReceiverWalletId = walletDom2.Wallet.WalletId,
                        Amount = 100
                    },
                    new ()
                    {
                        SenderWalletId = walletDom2.Wallet.WalletId,
                        ReceiverWalletId = walletDom3.Wallet.WalletId,
                        Amount = 150
                    },
                    new ()
                    {
                        SenderWalletId = walletDom3.Wallet.WalletId,
                        ReceiverWalletId = walletDom4.Wallet.WalletId,
                        Amount = 150
                    },
                    new ()
                    {
                        SenderWalletId = walletDom4.Wallet.WalletId,
                        ReceiverWalletId = walletDom5.Wallet.WalletId,
                        Amount = 30
                    },
                    new ()
                    {
                        SenderWalletId = walletDom5.Wallet.WalletId,
                        ReceiverWalletId = walletDom6.Wallet.WalletId,
                        Amount = 20
                    },
                    new ()
                    {
                        SenderWalletId = walletDom5.Wallet.WalletId,
                        ReceiverWalletId = walletDom2.Wallet.WalletId,
                        Amount = 40
                    }
                }
        };

        // create order
        await TestInit1.OrdersClient.CreateOrderAsync(TestInit1.AppId, request);

        // change token
        await TestInit1.AppsClient.ClearAllAsync(app.AppId);

        try
        {
            await TestInit1.AppsClient.GetAsync(app.AppId);
            Assert.Fail("App must be delete");
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
        }
    }
}