using EWallet.Api;
using EWallet.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class OrderScenariosTest : BaseControllerTest
{
    // document url: https://docs.google.com/spreadsheets/d/1vrY9GFmJwW-qulNaFsScPbPonDP4qdIJCtq4TCzBlLA/edit#gid=0
    [TestMethod]
    public async Task Void()
    {
        // Create wallets
        var systemWalletDom = await WalletDom.Create(TestInit1);
        var walletDom1 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom2 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom5 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom6 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);

        // Transfer to wallets in order to make available balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 100, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 150, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 20, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom5.Wallet.WalletId, amount: 40, transactionType: TransactionType.Sale);

        // Transfer to wallets in order to make pending balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 50, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 70, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 90, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 10, transactionType: TransactionType.Authorize);

        // Create order request
        var request = new CreateOrderRequest
        {
            OrderId = Guid.NewGuid(),
            CurrencyId = systemWalletDom.CurrencyId,
            TransactionType = TransactionType.Authorize,
            ParticipantWallets = new List<ParticipantTransferItem>
            {
                new()
                {
                    SenderWalletId = walletDom1.Wallet.WalletId,
                    ReceiverWalletId = walletDom5.Wallet.WalletId,
                    Amount = 100
                },
                new()
                {
                    SenderWalletId = walletDom2.Wallet.WalletId,
                    ReceiverWalletId = walletDom3.Wallet.WalletId,
                    Amount = 150
                },
                new()
                {
                    SenderWalletId = walletDom3.Wallet.WalletId,
                    ReceiverWalletId = walletDom4.Wallet.WalletId,
                    Amount = 150
                },
                new()
                {
                    SenderWalletId = walletDom4.Wallet.WalletId,
                    ReceiverWalletId = walletDom5.Wallet.WalletId,
                    Amount = 30
                },
                new()
                {
                    SenderWalletId = walletDom5.Wallet.WalletId,
                    ReceiverWalletId = walletDom6.Wallet.WalletId,
                    Amount = 20
                },
                new()
                {
                    SenderWalletId = walletDom5.Wallet.WalletId,
                    ReceiverWalletId = walletDom2.Wallet.WalletId,
                    Amount = 40
                }
            }
        };

        // create order
        var order = await TestInit1.OrdersClient.CreateOrderAsync(TestInit1.AppId, request);

        // void the order
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        // Get wallets after transfer
        var wallet1 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var wallet5 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom5.Wallet.WalletId);
        var wallet6 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom6.Wallet.WalletId);

        // Available balance of wallets after transfer
        ArgumentNullException.ThrowIfNull(wallet1.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4.Currencies);
        ArgumentNullException.ThrowIfNull(wallet5.Currencies);
        ArgumentNullException.ThrowIfNull(wallet6.Currencies);
        var wallet1AvailableBalance = wallet1.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet2AvailableBalance = wallet2.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet3AvailableBalance = wallet3.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet4AvailableBalance = wallet4.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet5AvailableBalance = wallet5.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet6AvailableBalance = wallet6.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;

        // Assert
        Assert.AreEqual(100, wallet1AvailableBalance);
        Assert.AreEqual(150, wallet2AvailableBalance);
        Assert.AreEqual(0, wallet3AvailableBalance);
        Assert.AreEqual(20, wallet4AvailableBalance);
        Assert.AreEqual(40, wallet5AvailableBalance);
        Assert.AreEqual(0, wallet6AvailableBalance);
    }

    [TestMethod]
    public async Task Sale()
    {
        // Create wallets
        var systemWalletDom = await WalletDom.Create(TestInit1);
        var walletDom1 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom2 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom3 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom4 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom5 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);
        var walletDom6 = await WalletDom.Create(TestInit1, minBalance: null, currencyId: systemWalletDom.CurrencyId);

        // Transfer to wallets in order to make available balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 100, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 150, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 20, transactionType: TransactionType.Sale);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom5.Wallet.WalletId, amount: 40, transactionType: TransactionType.Sale);

        // Transfer to wallets in order to make pending balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 50, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 70, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 90, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 10, transactionType: TransactionType.Authorize);

        // Create order request
        var request = new CreateOrderRequest
        {
            OrderId = Guid.NewGuid(),
            CurrencyId = systemWalletDom.CurrencyId,
            TransactionType = TransactionType.Sale,
            ParticipantWallets = new List<ParticipantTransferItem>
                {
                    new ()
                    {
                        SenderWalletId = walletDom1.Wallet.WalletId,
                        ReceiverWalletId = walletDom5.Wallet.WalletId,
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

        // Get wallets after transfer
        var wallet1 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var wallet5 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom5.Wallet.WalletId);
        var wallet6 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom6.Wallet.WalletId);

        // Available balance of wallets after transfer
        ArgumentNullException.ThrowIfNull(wallet1.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4.Currencies);
        ArgumentNullException.ThrowIfNull(wallet5.Currencies);
        ArgumentNullException.ThrowIfNull(wallet6.Currencies);
        var wallet1AvailableBalance = wallet1.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet2AvailableBalance = wallet2.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet3AvailableBalance = wallet3.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet4AvailableBalance = wallet4.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet5AvailableBalance = wallet5.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet6AvailableBalance = wallet6.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;

        // Assert
        Assert.AreEqual(0, wallet1AvailableBalance);
        Assert.AreEqual(140, wallet2AvailableBalance);
        Assert.AreEqual(0, wallet3AvailableBalance);
        Assert.AreEqual(140, wallet4AvailableBalance);
        Assert.AreEqual(10, wallet5AvailableBalance);
        Assert.AreEqual(20, wallet6AvailableBalance);
    }

    [TestMethod]
    public async Task Authorize()
    {
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

        // Transfer to wallets in order to make pending balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 50, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 70, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 90, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 10, transactionType: TransactionType.Authorize);

        // Create order request
        var request = new CreateOrderRequest
        {
            OrderId = Guid.NewGuid(),
            CurrencyId = systemWalletDom.CurrencyId,
            TransactionType = TransactionType.Authorize,
            ParticipantWallets = new List<ParticipantTransferItem>
                {
                    new ()
                    {
                        SenderWalletId = walletDom1.Wallet.WalletId,
                        ReceiverWalletId = walletDom5.Wallet.WalletId,
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

        // Get wallets after transfer
        var wallet1 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var wallet5 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom5.Wallet.WalletId);
        var wallet6 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom6.Wallet.WalletId);

        // Available balance of wallets after transfer
        ArgumentNullException.ThrowIfNull(wallet1.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4.Currencies);
        ArgumentNullException.ThrowIfNull(wallet5.Currencies);
        ArgumentNullException.ThrowIfNull(wallet6.Currencies);
        var wallet1AvailableBalance = wallet1.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet2AvailableBalance = wallet2.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet3AvailableBalance = wallet3.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet4AvailableBalance = wallet4.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet5AvailableBalance = wallet5.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet6AvailableBalance = wallet6.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;

        // Assert
        Assert.AreEqual(0, wallet1AvailableBalance);
        Assert.AreEqual(0, wallet2AvailableBalance);
        Assert.AreEqual(0, wallet3AvailableBalance);
        Assert.AreEqual(0, wallet4AvailableBalance);
        Assert.AreEqual(0, wallet5AvailableBalance);
        Assert.AreEqual(0, wallet6AvailableBalance);
    }
    [TestMethod]
    public async Task Capture()
    {
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

        // Transfer to wallets in order to make pending balances based on document
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom1.Wallet.WalletId, amount: 50, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom2.Wallet.WalletId, amount: 70, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom3.Wallet.WalletId, amount: 90, transactionType: TransactionType.Authorize);
        await systemWalletDom.CreateOrder(TestInit1, receiverWalletId: walletDom4.Wallet.WalletId, amount: 10, transactionType: TransactionType.Authorize);

        // Create order request
        var request = new CreateOrderRequest
        {
            CurrencyId = systemWalletDom.CurrencyId,
            TransactionType = TransactionType.Authorize,
            ParticipantWallets = new List<ParticipantTransferItem>
                {
                    new ()
                    {
                        SenderWalletId = walletDom1.Wallet.WalletId,
                        ReceiverWalletId = walletDom5.Wallet.WalletId,
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
        var order = await TestInit1.OrdersClient.CreateOrderAsync(TestInit1.AppId, request);

        // capture the order
        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, order.OrderId);

        // Get wallets after transfer
        var wallet1 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom1.Wallet.WalletId);
        var wallet2 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom2.Wallet.WalletId);
        var wallet3 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom3.Wallet.WalletId);
        var wallet4 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom4.Wallet.WalletId);
        var wallet5 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom5.Wallet.WalletId);
        var wallet6 = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom6.Wallet.WalletId);

        // Available balance of wallets after transfer
        ArgumentNullException.ThrowIfNull(wallet1.Currencies);
        ArgumentNullException.ThrowIfNull(wallet2.Currencies);
        ArgumentNullException.ThrowIfNull(wallet3.Currencies);
        ArgumentNullException.ThrowIfNull(wallet4.Currencies);
        ArgumentNullException.ThrowIfNull(wallet5.Currencies);
        ArgumentNullException.ThrowIfNull(wallet6.Currencies);
        var wallet1AvailableBalance = wallet1.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet2AvailableBalance = wallet2.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet3AvailableBalance = wallet3.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet4AvailableBalance = wallet4.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet5AvailableBalance = wallet5.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;
        var wallet6AvailableBalance = wallet6.Currencies.Single(c => c.CurrencyId == systemWalletDom.CurrencyId).Balance;

        // Assert
        Assert.AreEqual(0, wallet1AvailableBalance);
        Assert.AreEqual(140, wallet2AvailableBalance);
        Assert.AreEqual(0, wallet3AvailableBalance);
        Assert.AreEqual(140, wallet4AvailableBalance);
        Assert.AreEqual(10, wallet5AvailableBalance);
        Assert.AreEqual(20, wallet6AvailableBalance);
    }
}