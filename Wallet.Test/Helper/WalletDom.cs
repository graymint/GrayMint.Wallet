using EWallet.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Helper;

public class WalletDom
{
    public Wallet Wallet { get; set; }
    public Wallet? ReceiverWallet { get; set; }
    public int CurrencyId { get; set; }

    protected WalletDom(Wallet wallet, int currencyId)
    {
        Wallet = wallet;
        CurrencyId = currencyId;
    }

    public static async Task<WalletDom> Create(TestInit testInit, decimal? minBalance = -long.MaxValue, int? currencyId = null)
    {
        var wallet = await testInit.WalletsClient.CreateWalletAsync(testInit.AppId);
        var calculatedCurrencyId = currencyId ?? await testInit.CurrenciesClient.CreateAsync(testInit.AppId);

        // set min balance
        if (minBalance != null)
            await testInit.WalletsClient.SetMinBalanceAsync(testInit.AppId, wallet.WalletId, new SetMinBalanceRequest
            {
                CurrencyId = calculatedCurrencyId,
                MinBalance = (decimal)minBalance
            });

        // get wallet to prepare included objects
        wallet = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, wallet.WalletId);
        return new WalletDom(wallet, calculatedCurrencyId);
    }

    public async Task<Order> CreateOrder(TestInit testInit, int? senderWalletId = null, int? receiverWalletId = null, decimal? amount = null,
        int? currencyId = null, Guid? orderId = null, TransactionType transactionType = TransactionType.Authorize, int? orderTypeId = null)
    {
        var random = new Random();
        amount ??= random.Next(0, 100000);

        orderId ??= Guid.NewGuid();
        currencyId ??= CurrencyId;
        senderWalletId ??= Wallet.WalletId;
        if (receiverWalletId == null)
        {
            var walletDom = await Create(testInit, null);
            ReceiverWallet = walletDom.Wallet;
            receiverWalletId = ReceiverWallet.WalletId;
        }
        else
            ReceiverWallet = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)receiverWalletId);

        // get sender and receiver wallets info
        var app = await testInit.WalletDbContext.Apps.SingleAsync(x => x.AppId == testInit.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);

        var senderWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)senderWalletId);
        var receiverWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)receiverWalletId);
        var systemWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(senderWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(receiverWalletBefore.Currencies);

        orderTypeId ??= new Random().Next(int.MinValue, int.MaxValue);

        // create Sale order between created wallets
        var request = new CreateOrderRequest
        {
            OrderTypeId = orderTypeId.Value,
            CurrencyId = (int)currencyId,
            OrderId = (Guid)orderId,
            TransactionType = transactionType,
            ParticipantWallets = new List<ParticipantTransferItem>
            {
                new()
                {
                    SenderWalletId = (int)senderWalletId,
                    ReceiverWalletId = (int)receiverWalletId,
                    Amount =  (decimal)amount
                }
            }
        };
        var orderCreated = await testInit.OrdersClient.CreateOrderAsync(testInit.AppId, request);
        var order = await testInit.OrdersClient.GetOrderAsync(testInit.AppId, orderCreated.OrderId);

        // get wallets info after create order
        var systemWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);
        var senderWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)senderWalletId);
        var receiverWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)receiverWalletId);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(senderWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(receiverWalletAfter.Currencies);

        // validate sender wallet balance
        if (senderWalletBefore.Currencies.Single(x => x.CurrencyId == currencyId).Balance >= amount)
            Assert.AreEqual(senderWalletAfter.Currencies.Single(x => x.CurrencyId == currencyId).Balance,
                senderWalletBefore.Currencies.Single(x => x.CurrencyId == currencyId).Balance - amount);
        else
        {
            Assert.AreEqual(0, senderWalletAfter.Currencies.Single(x => x.CurrencyId == currencyId).Balance);
            Assert.AreEqual(senderWalletAfter.Currencies.Single(x => x.CurrencyId == currencyId).MinBalance,
                senderWalletBefore.Currencies.Single(x => x.CurrencyId == currencyId).MinBalance -
                senderWalletBefore.Currencies.Single(x => x.CurrencyId == currencyId).Balance +
                amount);
        }

        // validate sender wallet min balance
        switch (transactionType)
        {
            // validate
            case TransactionType.Authorize:

                // validate general properties
                Assert.IsNull(order.CapturedTime);
                Assert.IsNull(order.VoidedTime);
                Assert.AreEqual(OrderStatus.Authorized, order.Status);

                // validate receiver balance and minBalance
                Assert.AreEqual(receiverWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.MinBalance ?? 0,
                    receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.MinBalance ?? 0);

                Assert.AreEqual(receiverWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.Balance ?? 0,
                    receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.Balance ?? 0);

                // validate y=system wallet balance and min balance
                Assert.AreEqual(systemWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.MinBalance ?? 0,
                    systemWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.MinBalance ?? 0);

                Assert.AreEqual(systemWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.Balance ?? 0,
                    (systemWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.Balance ?? 0) + amount);

                break;

            case TransactionType.Sale:

                // validate general properties
                Assert.IsNotNull(order.CapturedTime);
                Assert.IsNull(order.VoidedTime);
                Assert.AreEqual(OrderStatus.Captured, order.Status);

                // validate minBalance
                Assert.AreEqual(receiverWalletAfter.Currencies.Single(x => x.CurrencyId == currencyId).MinBalance,
                    receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.MinBalance ?? 0);

                // validate balance
                Assert.AreEqual(receiverWalletAfter.Currencies.Single(x => x.CurrencyId == currencyId).Balance,
                    (receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId)?.Balance ?? 0) + amount);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(transactionType), transactionType, null);
        }

        return order;
    }

    public async Task<Order> Capture(TestInit testInit, Guid orderId)
    {
        var order = await testInit.OrdersClient.GetOrderAsync(testInit.AppId, orderId);

        // get sender and receiver wallets info
        var senderWalletId = order.Items.First().SenderWalletId;
        var receiverWalletId = order.Items.First().ReceiverWalletId;
        var senderWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, senderWalletId);
        var receiverWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, receiverWalletId);

        var app = await testInit.WalletDbContext.Apps.SingleAsync(x => x.AppId == testInit.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);
        var systemWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);

        // capture the order
        await testInit.OrdersClient.CaptureAsync(testInit.AppId, orderId);

        // get order info and validate
        order = await testInit.OrdersClient.GetOrderAsync(testInit.AppId, orderId);

        // get wallets info after capture
        var senderWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, senderWalletId);
        var receiverWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, receiverWalletId);
        var systemWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);
        ArgumentNullException.ThrowIfNull(receiverWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(receiverWalletBefore.Currencies);

        // validate sender wallet balance
        Assert.AreEqual(OrderStatus.Captured, order.Status);
        Assert.IsNotNull(order.CapturedTime);
        Assert.IsNull(order.VoidedTime);
        Assert.AreEqual(1, order.Items.Count);

        // validate balances
        Assert.AreEqual((senderWalletAfter.Currencies ?? throw new Exception("Currencies")).Single(x => x.CurrencyId == order.CurrencyId).Balance,
            (senderWalletBefore.Currencies ?? throw new Exception("Currencies")).Single(x => x.CurrencyId == order.CurrencyId).Balance);

        Assert.AreEqual(receiverWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == order.CurrencyId)?.Balance ?? 0,
            receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == order.CurrencyId)?.Balance ?? 0 + order.Items.First().Amount);

        Assert.AreEqual((systemWalletAfter.Currencies ?? throw new Exception("Currencies")).Single(x => x.CurrencyId == order.CurrencyId).Balance + order.Items.First().Amount,
            (systemWalletBefore.Currencies ?? throw new Exception("Currencies")).Single(x => x.CurrencyId == order.CurrencyId).Balance);

        return order;
    }

    public async Task Void(TestInit testInit, Guid orderId)
    {
        var order = await testInit.OrdersClient.GetOrderAsync(testInit.AppId, orderId);
        var amount = order.Items.First().Amount;

        // get sender and receiver wallets info
        var senderWalletId = order.Items.First().SenderWalletId;
        var receiverWalletId = order.Items.First().ReceiverWalletId;
        var senderWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, senderWalletId);
        var receiverWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, receiverWalletId);

        var app = await testInit.WalletDbContext.Apps.SingleAsync(x => x.AppId == testInit.AppId);
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);
        var systemWalletBefore = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);

        // capture the order
        await testInit.OrdersClient.VoidAsync(testInit.AppId, orderId);

        // get wallets info after void
        var senderWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, senderWalletId);
        var receiverWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, receiverWalletId);
        var systemWalletAfter = await testInit.WalletsClient.GetWalletAsync(testInit.AppId, (int)app.SystemWalletId);
        ArgumentNullException.ThrowIfNull(senderWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(senderWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(receiverWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(receiverWalletBefore.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletAfter.Currencies);
        ArgumentNullException.ThrowIfNull(systemWalletBefore.Currencies);

        // validate sender balance
        Assert.AreEqual(senderWalletAfter.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance,
            senderWalletBefore.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance + amount);

        if (order.Status == OrderStatus.Authorized)
        {
            // validate system balances
            Assert.AreEqual(systemWalletAfter.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance,
                systemWalletBefore.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance - amount);

            // validate receiver balances
            Assert.AreEqual(receiverWalletAfter.Currencies.SingleOrDefault(x => x.CurrencyId == order.CurrencyId)?.Balance ?? 0,
                receiverWalletBefore.Currencies.SingleOrDefault(x => x.CurrencyId == order.CurrencyId)?.Balance ?? 0);
        }
        if (order.Status == OrderStatus.Captured)
        {
            // validate system balances
            Assert.AreEqual(systemWalletAfter.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance,
                systemWalletBefore.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance);

            // validate receiver balances
            Assert.AreEqual(receiverWalletAfter.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance,
                receiverWalletBefore.Currencies.Single(x => x.CurrencyId == order.CurrencyId).Balance - amount);
        }

        // get order info again
        order = await testInit.OrdersClient.GetOrderAsync(testInit.AppId, orderId);

        Assert.AreEqual(OrderStatus.Voided, order.Status);
        Assert.IsNotNull(order.VoidedTime);
        Assert.AreEqual(1, order.Items.Count);
    }
}