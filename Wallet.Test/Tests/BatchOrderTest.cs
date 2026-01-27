using EWallet.Api;
using EWallet.Test.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class BatchOrderTest : BaseControllerTest
{
    [TestMethod]
    public async Task Success_create_batch_orders_with_Auth()
    {
        // create wallets
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);

        var app = await TestInit1.WalletDbContext.Apps.SingleAsync(x => x.AppId == TestInit1.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletBefore = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

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
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Authorize
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Authorize
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // validate orders
        var ordersList = orders.ToList();
        Assert.AreEqual(2, ordersList.Count);
        Assert.AreEqual(OrderStatus.Authorized, ordersList[0].Status);
        Assert.AreEqual(OrderStatus.Authorized, ordersList[1].Status);
        Assert.AreEqual(orderId1, ordersList[0].OrderId);
        Assert.AreEqual(orderId2, ordersList[1].OrderId);

        // get wallets info after create orders
        var wallet1After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletAfter = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4After.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);

        // validate wallet1 balance (sender - should decrease)
        Assert.AreEqual(wallet1After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount1);

        // validate wallet2 balance (receiver in Authorize should not change)
        Assert.AreEqual(wallet2After.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0,
            wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0);

        // validate wallet3 balance (sender - should decrease)
        Assert.AreEqual(wallet3After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount2);

        // validate wallet4 balance (receiver in Authorize should not change)
        Assert.AreEqual(wallet4After.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0,
            wallet4Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0);

        // validate system wallet balance (should increase by both amounts in Authorize)
        Assert.AreEqual(systemWalletAfter.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (systemWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount1 + amount2);
    }

    [TestMethod]
    public async Task Success_create_batch_orders_with_Capture()
    {
        // create wallets
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var amount1 = 100m;
        var amount2 = 200m;
        var random = new Random();
        var orderTypeId1 = random.Next(int.MinValue, int.MaxValue);
        var orderTypeId2 = random.Next(int.MinValue, int.MaxValue);

        // create batch orders with Authorize first
        var requests = new[]
        {
            new CreateSingleTransferRequest
            {
                OrderId = orderId1,
                SenderWalletId = walletDom1.Wallet.WalletId,
                ReceiverWalletId = walletDom2.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Authorize
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Authorize
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

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
        var wallet1After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4After.Currencies);

        // validate wallet1 balance (sender - should decrease)
        Assert.AreEqual(wallet1After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount1);

        // validate wallet2 balance (receiver after capture should increase)
        Assert.AreEqual(wallet2After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount1);

        // validate wallet3 balance (sender - should decrease)
        Assert.AreEqual(wallet3After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount2);

        // validate wallet4 balance (receiver after capture should increase)
        Assert.AreEqual(wallet4After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (wallet4Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount2);
    }

    [TestMethod]
    public async Task Success_create_batch_orders_with_Sale()
    {
        // create wallets
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var amount1 = 100m;
        var amount2 = 200m;
        var random = new Random();
        var orderTypeId1 = random.Next(int.MinValue, int.MaxValue);
        var orderTypeId2 = random.Next(int.MinValue, int.MaxValue);

        // create batch orders with Sale (automatically captured)
        var requests = new[]
        {
            new CreateSingleTransferRequest
            {
                OrderId = orderId1,
                SenderWalletId = walletDom1.Wallet.WalletId,
                ReceiverWalletId = walletDom2.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Sale
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Sale
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // validate orders
        var ordersList = orders.ToList();
        Assert.AreEqual(2, ordersList.Count);
        Assert.AreEqual(OrderStatus.Captured, ordersList[0].Status);
        Assert.AreEqual(OrderStatus.Captured, ordersList[1].Status);
        Assert.IsNotNull(ordersList[0].CapturedTime);
        Assert.IsNotNull(ordersList[1].CapturedTime);

        // get wallets info after create orders
        var wallet1After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4After.Currencies);

        // validate wallet balances
        Assert.AreEqual(wallet1After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount1);

        Assert.AreEqual(wallet2After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount1);

        Assert.AreEqual(wallet3After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount2);

        Assert.AreEqual(wallet4After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (wallet4Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount2);
    }

    [TestMethod]
    public async Task Success_create_two_orders_and_void_one()
    {
        // create wallets
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);

        var app = await TestInit1.WalletDbContext.Apps.SingleAsync(x => x.AppId == TestInit1.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        // get wallets info before create orders
        var wallet1Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var systemWalletBefore = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

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
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Authorize
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Authorize
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // void first order
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, orderId1);

        // get order info after void
        var order1AfterVoid = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId1);
        var order2 = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId2);

        // validate order statuses
        Assert.AreEqual(OrderStatus.Voided, order1AfterVoid.Status);
        Assert.AreEqual(OrderStatus.Authorized, order2.Status);
        Assert.IsNotNull(order1AfterVoid.VoidedTime);
        Assert.IsNull(order2.VoidedTime);

        // get wallets info after void
        var wallet1After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet3After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var systemWalletAfter = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(wallet1Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet1After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3After.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);

        // validate wallet1 balance (should be back to original after void)
        Assert.AreEqual(wallet1After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet1Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance);

        // validate wallet3 balance (should still be reduced as order2 is not voided)
        Assert.AreEqual(wallet3After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            wallet3Before.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance - amount2);

        // validate system wallet balance (should have only amount2)
        Assert.AreEqual(systemWalletAfter.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (systemWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount2);
    }

    [TestMethod]
    public async Task Success_create_two_orders_and_capture_one()
    {
        // create wallets
        var walletDom1 = await WalletDom.Create(TestInit1);
        var walletDom2 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, currencyId: walletDom1.CurrencyId);

        var app = await TestInit1.WalletDbContext.Apps.SingleAsync(x => x.AppId == TestInit1.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        // get wallets info before create orders
        var wallet2Before = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var systemWalletBefore = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

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
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount1,
                OrderTypeId = orderTypeId1,
                TransactionType = TransactionType.Authorize
            },
            new CreateSingleTransferRequest
            {
                OrderId = orderId2,
                SenderWalletId = walletDom3.Wallet.WalletId,
                ReceiverWalletId = walletDom4.Wallet.WalletId,
                CurrencyId = walletDom1.CurrencyId,
                Amount = amount2,
                OrderTypeId = orderTypeId2,
                TransactionType = TransactionType.Authorize
            }
        };

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId, requests);

        // capture first order
        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, orderId1);

        // get order info after capture
        var order1AfterCapture = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId1);
        var order2 = await TestInit1.OrdersClient.GetOrderAsync(TestInit1.AppId, orderId2);

        // validate order statuses
        Assert.AreEqual(OrderStatus.Captured, order1AfterCapture.Status);
        Assert.AreEqual(OrderStatus.Authorized, order2.Status);
        Assert.IsNotNull(order1AfterCapture.CapturedTime);
        Assert.IsNull(order2.CapturedTime);

        // get wallets info after capture
        var wallet2After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet4After = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var systemWalletAfter = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, (int)app.SystemWalletId);

        ArgumentNullException.ThrowIfNull(wallet2Before.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2After.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4After.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);

        // validate wallet2 balance (should increase after capture)
        Assert.AreEqual(wallet2After.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (wallet2Before.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount1);

        // validate wallet4 balance (should not change as order2 is not captured)
        Assert.AreEqual(wallet4After.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0, 0);

        // validate system wallet balance (should still have only amount2 because amount1 was moved to wallet2)
        Assert.AreEqual(systemWalletAfter.Currencies.Single(x => x.CurrencyId == walletDom1.CurrencyId).Balance,
            (systemWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == walletDom1.CurrencyId)?.Balance ?? 0) + amount2);
    }
}
