using System.Net.Http.Json;
using EWallet.Api;
using EWallet.Exceptions;
using EWallet.Test.Helper;
using GrayMint.Common.ApiClients;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Test.Tests;

[TestClass]
public class DebtOrderTest : OrderTestBase
{
    [TestMethod]
    public async Task Sale_with_AllowDebt_makes_sender_negative_when_it_has_no_balance() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        var order = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);

        Assert.AreEqual(OrderStatus.Captured, order.Status);
        var sender = await GetBalance(senderId, currencyId);
        Assert.IsNotNull(sender);
        Assert.AreEqual(-300, sender.Balance);
        Assert.AreEqual(0, sender.MinBalance);
        Assert.AreEqual(300, (await GetBalance(receiverId, currencyId))?.Balance);

        // the ledger snapshot must record the debt, not 0
        var senderTransaction = await TestInit1.WalletDbContext.WalletTransactions
            .AsNoTracking()
            .SingleAsync(x => x.WalletId == senderId);
        Assert.AreEqual(-300, senderTransaction.Amount);
        Assert.AreEqual(-300, senderTransaction.Balance);
    }

    [TestMethod]
    public async Task Sale_with_AllowDebt_spends_the_existing_balance_before_going_negative() {
        var treasury = await WalletDom.Create(TestInit1);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await treasury.CreateOrder(TestInit1, receiverWalletId: senderId, amount: 100, transactionType: TransactionType.Sale);

        await CreateOrder(senderId, receiverId, treasury.CurrencyId, amount: 300, allowDebt: true);

        Assert.AreEqual(-200, (await GetBalance(senderId, treasury.CurrencyId))?.Balance);
        Assert.AreEqual(300, (await GetBalance(receiverId, treasury.CurrencyId))?.Balance);
    }

    [TestMethod]
    public async Task Sale_with_AllowDebt_uses_MinBalance_before_going_into_debt() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await SetMinBalance(senderId, currencyId, -1000);

        await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);

        await AssertBalance(senderId, currencyId, balance: 0, minBalance: -700);
    }

    [TestMethod]
    public async Task Sale_with_AllowDebt_spends_balance_then_MinBalance_then_goes_into_debt() {
        var treasury = await WalletDom.Create(TestInit1);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await SetMinBalance(senderId, treasury.CurrencyId, -1000);
        await treasury.CreateOrder(TestInit1, receiverWalletId: senderId, amount: 100, transactionType: TransactionType.Sale);

        // 1500 = 100 from balance + 1000 from MinBalance + 400 of debt
        await CreateOrder(senderId, receiverId, treasury.CurrencyId, amount: 1500, allowDebt: true);

        await AssertBalance(senderId, treasury.CurrencyId, balance: -400, minBalance: 0);
        await AssertBalance(receiverId, treasury.CurrencyId, balance: 1500, minBalance: 0);
    }

    [TestMethod]
    public async Task Incoming_funds_repay_the_debt() {
        var treasury = await WalletDom.Create(TestInit1);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await CreateOrder(senderId, receiverId, treasury.CurrencyId, amount: 300, allowDebt: true);

        await treasury.CreateOrder(TestInit1, receiverWalletId: senderId, amount: 500, transactionType: TransactionType.Sale);

        var sender = await GetBalance(senderId, treasury.CurrencyId);
        Assert.IsNotNull(sender);
        Assert.AreEqual(200, sender.Balance);
        Assert.AreEqual(0, sender.MinBalance);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(false)]
    public async Task Fail_when_AllowDebt_is_not_set_and_balance_is_insufficient(bool? allowDebt) {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: allowDebt));

        Assert.IsNull(await GetBalance(senderId, currencyId));
    }

    [TestMethod]
    public async Task Fail_when_AllowDebt_is_missing_from_the_request_body() {
        // clients generated before AllowDebt existed do not send the field at all
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        var response = await TestInit1.HttpClient.PostAsJsonAsync($"api/apps/{TestInit1.AppId}/orders", new
        {
            orderId = Guid.NewGuid(),
            currencyId,
            orderTypeId = 1,
            transactionType = "Sale",
            participantWallets = new[] { new { senderWalletId = senderId, receiverWalletId = receiverId, amount = 300m } }
        });

        Assert.IsFalse(response.IsSuccessStatusCode);
        var apiError = ApiError.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(nameof(InsufficientBalanceException), apiError.TypeName);
        Assert.IsNull(await GetBalance(senderId, currencyId));
    }

    [TestMethod]
    public async Task Fail_normal_payment_from_a_wallet_in_debt() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            CreateOrder(senderId, receiverId, currencyId, amount: 10, allowDebt: null));

        Assert.AreEqual(-300, (await GetBalance(senderId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Normal_payment_from_a_wallet_in_debt_uses_MinBalance_and_keeps_the_debt() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);

        // the MinBalance is granted after the wallet went into debt
        await SetMinBalance(senderId, currencyId, -1000);

        await CreateOrder(senderId, receiverId, currencyId, amount: 100, allowDebt: null);

        var sender = await GetBalance(senderId, currencyId);
        Assert.IsNotNull(sender);
        Assert.AreEqual(-300, sender.Balance);
        Assert.AreEqual(-900, sender.MinBalance);
        Assert.AreEqual(400, (await GetBalance(receiverId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Fail_normal_payment_from_a_wallet_in_debt_beyond_its_MinBalance() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);

        // the MinBalance is granted after the wallet went into debt
        await SetMinBalance(senderId, currencyId, -1000);

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            CreateOrder(senderId, receiverId, currencyId, amount: 1001, allowDebt: null));

        var sender = await GetBalance(senderId, currencyId);
        Assert.IsNotNull(sender);
        Assert.AreEqual(-300, sender.Balance);
        Assert.AreEqual(-1000, sender.MinBalance);
    }

    [TestMethod]
    public async Task Authorize_with_AllowDebt_holds_funds_until_capture() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        var systemBefore = await GetBalance(TestInit1.SystemWalletId, currencyId);
        Assert.IsNotNull(systemBefore);

        var order = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true,
            transactionType: TransactionType.Authorize);

        Assert.AreEqual(OrderStatus.Authorized, order.Status);
        Assert.AreEqual(-300, (await GetBalance(senderId, currencyId))?.Balance);
        Assert.IsNull(await GetBalance(receiverId, currencyId));
        Assert.AreEqual(systemBefore.Balance + 300, (await GetBalance(TestInit1.SystemWalletId, currencyId))?.Balance);

        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, order.OrderId);

        Assert.AreEqual(-300, (await GetBalance(senderId, currencyId))?.Balance);
        Assert.AreEqual(300, (await GetBalance(receiverId, currencyId))?.Balance);
        Assert.AreEqual(systemBefore.Balance, (await GetBalance(TestInit1.SystemWalletId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Void_an_authorized_debt_order_clears_the_debt() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        var systemBefore = await GetBalance(TestInit1.SystemWalletId, currencyId);
        Assert.IsNotNull(systemBefore);

        var order = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true,
            transactionType: TransactionType.Authorize);
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        Assert.AreEqual(0, (await GetBalance(senderId, currencyId))?.Balance);
        Assert.IsNull(await GetBalance(receiverId, currencyId));
        Assert.AreEqual(systemBefore.Balance, (await GetBalance(TestInit1.SystemWalletId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Void_a_captured_debt_order_clears_the_debt() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        var order = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true);
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        Assert.AreEqual(0, (await GetBalance(senderId, currencyId))?.Balance);
        Assert.AreEqual(0, (await GetBalance(receiverId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Retrying_a_debt_order_does_not_debit_twice() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();
        var orderId = Guid.NewGuid();

        var order = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true, orderId: orderId);
        var retriedOrder = await CreateOrder(senderId, receiverId, currencyId, amount: 300, allowDebt: true, orderId: orderId);

        Assert.AreEqual(order.OrderId, retriedOrder.OrderId);
        Assert.AreEqual(-300, (await GetBalance(senderId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Fail_multi_leg_order_with_AllowDebt() {
        var treasury = await WalletDom.Create(TestInit1);
        var walletAId = await CreateWallet();
        var walletBId = await CreateWallet();
        var walletCId = await CreateWallet();
        await treasury.CreateOrder(TestInit1, receiverWalletId: walletAId, amount: 100, transactionType: TransactionType.Sale);

        // the order would succeed without AllowDebt; it must be rejected only because it has more than one leg
        await TestUtil.AssertApiException<InvalidOperationException>(
            CreateOrder(treasury.CurrencyId, allowDebt: true,
                Leg(walletAId, walletBId, 100),
                Leg(walletBId, walletCId, 5)));

        Assert.AreEqual(100, (await GetBalance(walletAId, treasury.CurrencyId))?.Balance);
        Assert.IsNull(await GetBalance(walletBId, treasury.CurrencyId));
        Assert.IsNull(await GetBalance(walletCId, treasury.CurrencyId));
    }

    [TestMethod]
    public async Task Multi_leg_order_with_AllowDebt_false_works_as_before() {
        var treasury = await WalletDom.Create(TestInit1);
        var walletAId = await CreateWallet();
        var walletBId = await CreateWallet();
        var walletCId = await CreateWallet();
        await treasury.CreateOrder(TestInit1, receiverWalletId: walletAId, amount: 100, transactionType: TransactionType.Sale);

        await CreateOrder(treasury.CurrencyId, allowDebt: false,
            Leg(walletAId, walletBId, 100),
            Leg(walletBId, walletCId, 5));

        Assert.AreEqual(0, (await GetBalance(walletAId, treasury.CurrencyId))?.Balance);
        Assert.AreEqual(95, (await GetBalance(walletBId, treasury.CurrencyId))?.Balance);
        Assert.AreEqual(5, (await GetBalance(walletCId, treasury.CurrencyId))?.Balance);
    }

    [TestMethod]
    public async Task Batch_with_AllowDebt_makes_sender_negative() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        var orders = await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId,
        [
            CreateTransfer(senderId, receiverId, currencyId, amount: 300, allowDebt: true)
        ]);

        Assert.AreEqual(OrderStatus.Captured, orders.Single().Status);
        Assert.AreEqual(-300, (await GetBalance(senderId, currencyId))?.Balance);
        Assert.AreEqual(300, (await GetBalance(receiverId, currencyId))?.Balance);
    }

    [TestMethod]
    public async Task Batch_applies_AllowDebt_per_item_and_rolls_back_as_a_whole() {
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var debtorId = await CreateWallet();
        var senderId = await CreateWallet();
        var receiverId = await CreateWallet();

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId,
            [
                CreateTransfer(debtorId, receiverId, currencyId, amount: 300, allowDebt: true),
                CreateTransfer(senderId, receiverId, currencyId, amount: 100, allowDebt: null)
            ]));

        Assert.IsNull(await GetBalance(debtorId, currencyId));
        Assert.IsNull(await GetBalance(receiverId, currencyId));
    }

    [TestMethod]
    public async Task Batch_can_put_a_wallet_in_debt_and_forward_the_funds() {
        // the batch replacement for a multi-leg debt order: items run in order inside one transaction
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var walletAId = await CreateWallet();
        var walletBId = await CreateWallet();
        var walletCId = await CreateWallet();

        await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId,
        [
            CreateTransfer(walletAId, walletBId, currencyId, amount: 100, allowDebt: true),
            CreateTransfer(walletBId, walletCId, currencyId, amount: 30, allowDebt: null)
        ]);

        Assert.AreEqual(-100, (await GetBalance(walletAId, currencyId))?.Balance);
        Assert.AreEqual(70, (await GetBalance(walletBId, currencyId))?.Balance);
        Assert.AreEqual(30, (await GetBalance(walletCId, currencyId))?.Balance);
    }
}
