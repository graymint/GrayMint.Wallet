using EWallet.Api;
using EWallet.Test.Helper;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Test.Tests;

[TestClass]
public class BatchOrderTest : BaseControllerTest
{
    [TestMethod]
    public async Task Success_create_batch_orders_with_Auth_and_Capture() {
        // create system wallets
        var systemWalletDom = await WalletDom.Create(TestInit1);
        var currencyId1 = systemWalletDom.CurrencyId;
        var currencyId2 = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, systemWalletDom.Wallet.WalletId,
            new SetMinBalanceRequest
            {
                CurrencyId = currencyId1,
                MinBalance = long.MinValue
            });
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, systemWalletDom.Wallet.WalletId,
            new SetMinBalanceRequest
            {
                CurrencyId = currencyId2,
                MinBalance = long.MinValue
            });

        // create child wallets

        var walletDom1 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom2 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom3 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom4 = await WalletDom.Create(TestInit1, minBalance: null);

        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 1000, currencyId: currencyId1, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 1000, currencyId: currencyId2, transactionType: TransactionType.Sale);

        var app = await TestInit1.WalletDbContext.Apps.SingleAsync(x => x.AppId == TestInit1.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletBefore = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        Assert.AreEqual(1000, wallet1Before.Currencies?.Single(x => x.CurrencyId == currencyId1).Balance);
        Assert.AreEqual(1000, wallet3Before.Currencies?.Single(x => x.CurrencyId == currencyId2).Balance);

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var amount1 = 100m;
        var amount2 = 200m;
        var random = new Random();
        var orderTypeId1 = random.Next(int.MinValue, int.MaxValue);
        var orderTypeId2 = random.Next(int.MinValue, int.MaxValue);

        // create batch orders with Authorize
        var requests = new[]
        {
            new CreateSingleTransferRequest
            {
                OrderId = orderId1,
                SenderWalletId = walletDom1.Wallet.WalletId,
                ReceiverWalletId = walletDom2.Wallet.WalletId,
                CurrencyId = currencyId1,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Authorize
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = currencyId2,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Authorize
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // validate orders
        var ordersList = orders.ToList();
        Assert.HasCount(2, ordersList);
        Assert.AreEqual(OrderStatus.Authorized, ordersList[0].Status);
        Assert.AreEqual(OrderStatus.Authorized, ordersList[1].Status);
        Assert.AreEqual(orderId1, ordersList[0].OrderId);
        Assert.AreEqual(orderId2, ordersList[1].OrderId);

        // get wallets info after create orders
        var wallet1AfterAuth = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2AfterAuth = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3AfterAuth = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4AfterAuth = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletAfter = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1AfterAuth.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2AfterAuth.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3AfterAuth.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4AfterAuth.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);

        // validate wallet1 balance (sender - should decrease)
        Assert.AreEqual(wallet1AfterAuth.Currencies.Single(x => x.CurrencyId == currencyId1).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == currencyId1).Balance - amount1);

        // validate wallet2 balance (receiver in Authorize should not change)
        Assert.AreEqual(wallet2AfterAuth.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0,
            wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0);

        // validate wallet3 balance (sender - should decrease)
        Assert.AreEqual(wallet3AfterAuth.Currencies.Single(x => x.CurrencyId == currencyId2).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == currencyId2).Balance - amount2);

        // validate wallet4 balance (receiver in Authorize should not change)
        Assert.AreEqual(wallet4AfterAuth.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0,
            wallet4Before.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0);

        // capture both orders
        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, orderId1);
        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, orderId2);

        // get orders after capture
        var order1 = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId1);
        var order2 = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId2);

        // validate orders
        Assert.AreEqual(OrderStatus.Captured, order1.Status);
        Assert.AreEqual(OrderStatus.Captured, order2.Status);
        Assert.IsNotNull(order1.CapturedTime);
        Assert.IsNotNull(order2.CapturedTime);

        // get wallets info after capture
        var wallet1AfterCapture = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2AfterCapture = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3AfterCapture = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4AfterCapture = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);

        ArgumentNullException.ThrowIfNull(wallet1AfterCapture.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2AfterCapture.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3AfterCapture.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4AfterCapture.Currencies);

        // validate wallet1 balance (sender - should decrease)
        Assert.AreEqual(wallet1AfterCapture.Currencies.Single(x => x.CurrencyId == currencyId1).Balance,
            wallet1AfterAuth.Currencies.Single(x => x.CurrencyId == currencyId1).Balance);

        // validate wallet2 balance (receiver after capture should increase)
        Assert.AreEqual(wallet2AfterCapture.Currencies.Single(x => x.CurrencyId == currencyId1).Balance,
            (wallet2AfterAuth.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId1)?.Balance ?? 0) + amount1);

        // validate wallet3 balance (sender - should decrease)
        Assert.AreEqual(wallet3AfterCapture.Currencies.Single(x => x.CurrencyId == currencyId2).Balance,
            wallet3AfterAuth.Currencies.Single(x => x.CurrencyId == currencyId2).Balance);

        // validate wallet4 balance (receiver after capture should increase)
        Assert.AreEqual(wallet4AfterCapture.Currencies.Single(x => x.CurrencyId == currencyId2).Balance,
            (wallet4AfterAuth.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0) + amount2);
    }

    [TestMethod]
    public async Task Success_create_batch_orders_with_Sale() {
        // create system wallets
        var systemWalletDom = await WalletDom.Create(TestInit1);
        var currencyId1 = systemWalletDom.CurrencyId;
        var currencyId2 = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, systemWalletDom.Wallet.WalletId,
            new SetMinBalanceRequest
            {
                CurrencyId = currencyId1,
                MinBalance = long.MinValue
            });
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, systemWalletDom.Wallet.WalletId,
            new SetMinBalanceRequest
            {
                CurrencyId = currencyId2,
                MinBalance = long.MinValue
            });

        // create child wallets

        var walletDom1 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom2 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom3 = await WalletDom.Create(TestInit1, minBalance: null);
        var walletDom4 = await WalletDom.Create(TestInit1, minBalance: null);

        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 1000, currencyId: currencyId1, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 1000, currencyId: currencyId2, transactionType: TransactionType.Sale);

        var app = await TestInit1.WalletDbContext.Apps.SingleAsync(x => x.AppId == TestInit1.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletBefore = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        Assert.AreEqual(1000, wallet1Before.Currencies?.Single(x => x.CurrencyId == currencyId1).Balance);
        Assert.AreEqual(1000, wallet3Before.Currencies?.Single(x => x.CurrencyId == currencyId2).Balance);

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var amount1 = 100m;
        var amount2 = 200m;
        var random = new Random();
        var orderTypeId1 = random.Next(int.MinValue, int.MaxValue);
        var orderTypeId2 = random.Next(int.MinValue, int.MaxValue);

        // create batch orders with Authorize
        var requests = new[]
        {
            new CreateSingleTransferRequest
            {
                OrderId = orderId1,
                SenderWalletId = walletDom1.Wallet.WalletId,
                ReceiverWalletId = walletDom2.Wallet.WalletId,
                CurrencyId = currencyId1,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Sale
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = currencyId2,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Sale
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // validate orders
        var ordersList = orders.ToList();
        Assert.HasCount(2, ordersList);
        Assert.AreEqual(OrderStatus.Captured, ordersList[0].Status);
        Assert.AreEqual(OrderStatus.Captured, ordersList[1].Status);
        Assert.AreEqual(orderId1, ordersList[0].OrderId);
        Assert.AreEqual(orderId2, ordersList[1].OrderId);

        // get wallets info after create orders
        var wallet1AfterSale = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2AfterSale = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3AfterSale = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4AfterSale = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletAfter = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1AfterSale.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2AfterSale.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3AfterSale.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4AfterSale.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);

        // validate wallet1 balance (sender - should decrease)
        Assert.AreEqual(wallet1AfterSale.Currencies.Single(x => x.CurrencyId == currencyId1).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == currencyId1).Balance - amount1);

        // validate wallet2 balance (receiver after capture should increase)
        Assert.AreEqual(wallet2AfterSale.Currencies.Single(x => x.CurrencyId == currencyId1).Balance,
            (wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId1)?.Balance ?? 0) + amount1);

        // validate wallet3 balance (sender - should decrease)
        Assert.AreEqual(wallet3AfterSale.Currencies.Single(x => x.CurrencyId == currencyId2).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == currencyId2).Balance - amount2);

        // validate wallet4 balance (receiver after capture should increase)
        Assert.AreEqual(wallet4AfterSale.Currencies.Single(x => x.CurrencyId == currencyId2).Balance,
            (wallet4Before.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId2)?.Balance ?? 0) + amount2);
    }
}
