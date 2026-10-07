using EWallet.Api;
using EWallet.Exceptions;
using EWallet.Test.Helper;
using GrayMint.Common.Utils;

namespace EWallet.Test.Tests;

// Every test starts with an admin wallet that has MinBalance -1000 and a child wallet that has MinBalance -500,
// both with a zero balance. The shop wallet, when used, has no MinBalance.
// A sender always pays from its positive balance first, then its MinBalance, and only then goes into debt
// when the transfer allows it (AllowDebt or a void).
[TestClass]
public class AdminChildTest : OrderTestBase
{
    [TestMethod]
    public async Task Admin_pays_child_from_its_MinBalance() {
        var wallets = await CreateAdminAndChild();

        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 300, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_spends_its_balance_before_its_MinBalance() {
        var wallets = await CreateAdminAndChild();
        await wallets.Treasury.CreateOrder(TestInit1, receiverWalletId: wallets.AdminId, amount: 100, transactionType: TransactionType.Sale);

        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -800);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 300, minBalance: -500);
    }

    [TestMethod]
    public async Task Fail_admin_pays_child_beyond_its_MinBalance() {
        var wallets = await CreateAdminAndChild();

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1001, allowDebt: null));

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -1000);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_with_AllowDebt_uses_its_MinBalance_while_it_covers_the_payment() {
        var wallets = await CreateAdminAndChild();

        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: true);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 300, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_with_AllowDebt_goes_into_debt_after_its_MinBalance_is_used_up() {
        var wallets = await CreateAdminAndChild();

        // 1300 = 1000 from MinBalance + 300 of debt
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1300, allowDebt: true);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -300, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 1300, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_with_AllowDebt_spends_balance_then_MinBalance_then_goes_into_debt() {
        var wallets = await CreateAdminAndChild();
        await wallets.Treasury.CreateOrder(TestInit1, receiverWalletId: wallets.AdminId, amount: 100, transactionType: TransactionType.Sale);

        // 1500 = 100 from balance + 1000 from MinBalance + 400 of debt
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1500, allowDebt: true);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -400, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 1500, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_debt_is_not_limited() {
        var wallets = await CreateAdminAndChild();

        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 5000, allowDebt: true);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -4000, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 5000, minBalance: -500);
    }

    [TestMethod]
    public async Task Fail_admin_in_debt_pays_child_without_AllowDebt() {
        var wallets = await CreateAdminAndChild();
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1300, allowDebt: true);

        await TestUtil.AssertApiException<InsufficientBalanceException>(
            CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1, allowDebt: null));

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -300, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 1300, minBalance: -500);
    }

    [TestMethod]
    public async Task Child_spends_what_it_received_and_then_its_own_MinBalance() {
        var wallets = await CreateAdminAndChild();
        var shopId = await CreateWallet();
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await CreateOrder(wallets.ChildId, shopId, wallets.CurrencyId, amount: 700, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -100);
        await AssertBalance(shopId, wallets.CurrencyId, balance: 700, minBalance: 0);
    }

    [TestMethod]
    public async Task Admin_payment_repays_child_debt() {
        var wallets = await CreateAdminAndChild();
        var shopId = await CreateWallet();

        // 700 = 500 from the child's MinBalance + 200 of debt
        await CreateOrder(wallets.ChildId, shopId, wallets.CurrencyId, amount: 700, allowDebt: true);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: -200, minBalance: 0);

        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 100, minBalance: 0);
    }

    [TestMethod]
    public async Task Child_repayment_clears_admin_debt_but_not_its_used_MinBalance() {
        var wallets = await CreateAdminAndChild();
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1300, allowDebt: true);

        await CreateOrder(wallets.ChildId, wallets.AdminId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 0, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 1000, minBalance: -500);
    }

    [TestMethod]
    public async Task Child_repaying_admin_does_not_give_back_admin_MinBalance() {
        // receiving funds never restores a used MinBalance
        var wallets = await CreateAdminAndChild();
        await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await CreateOrder(wallets.ChildId, wallets.AdminId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 300, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -500);
    }

    [TestMethod]
    public async Task Void_admin_debt_payment_clears_the_debt_but_not_the_used_MinBalance() {
        var wallets = await CreateAdminAndChild();
        var order = await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1300, allowDebt: true);

        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 1000, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -500);
    }

    [TestMethod]
    public async Task Void_admin_payment_from_MinBalance_returns_funds_to_admin_balance() {
        // a void puts the funds back in Balance, not in the MinBalance they were paid from
        var wallets = await CreateAdminAndChild();
        var order = await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);

        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 300, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -500);
    }

    [TestMethod]
    public async Task Void_after_child_spent_part_of_the_funds_takes_balance_then_MinBalance() {
        var wallets = await CreateAdminAndChild();
        var shopId = await CreateWallet();
        var order = await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null);
        await CreateOrder(wallets.ChildId, shopId, wallets.CurrencyId, amount: 200, allowDebt: null);

        // the 300 clawback = the 100 the child still has + 200 from its MinBalance
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 300, minBalance: -700);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -300);
        await AssertBalance(shopId, wallets.CurrencyId, balance: 200, minBalance: 0);
    }

    [TestMethod]
    public async Task Void_after_child_spent_more_than_its_MinBalance_takes_the_rest_as_debt() {
        var wallets = await CreateAdminAndChild();
        var shopId = await CreateWallet();
        var order = await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 800, allowDebt: null);
        await CreateOrder(wallets.ChildId, shopId, wallets.CurrencyId, amount: 800, allowDebt: null);

        // the 800 clawback = 500 from the child's MinBalance + 300 of debt
        await TestInit1.OrdersClient.VoidAsync(TestInit1.AppId, order.OrderId);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: 800, minBalance: -200);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: -300, minBalance: 0);
        await AssertBalance(shopId, wallets.CurrencyId, balance: 800, minBalance: 0);
    }

    [TestMethod]
    public async Task Batch_admin_pays_one_child_from_MinBalance_and_another_with_debt() {
        var wallets = await CreateAdminAndChild();
        var child2Id = await CreateWallet();
        await SetMinBalance(child2Id, wallets.CurrencyId, -500);

        // the second transfer uses the 700 left of the admin's MinBalance and puts the other 1300 into debt
        await TestInit1.OrdersClient.CreateBatchOrdersAsync(TestInit1.AppId,
        [
            CreateTransfer(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 300, allowDebt: null),
            CreateTransfer(wallets.AdminId, child2Id, wallets.CurrencyId, amount: 2000, allowDebt: true)
        ]);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -1300, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 300, minBalance: -500);
        await AssertBalance(child2Id, wallets.CurrencyId, balance: 2000, minBalance: -500);
    }

    [TestMethod]
    public async Task Admin_authorizes_with_debt_and_child_receives_on_capture() {
        var wallets = await CreateAdminAndChild();

        var order = await CreateOrder(wallets.AdminId, wallets.ChildId, wallets.CurrencyId, amount: 1300, allowDebt: true,
            transactionType: TransactionType.Authorize);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -300, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 0, minBalance: -500);

        await TestInit1.OrdersClient.CaptureAsync(TestInit1.AppId, order.OrderId);

        await AssertBalance(wallets.AdminId, wallets.CurrencyId, balance: -300, minBalance: 0);
        await AssertBalance(wallets.ChildId, wallets.CurrencyId, balance: 1300, minBalance: -500);
    }

    private async Task<AdminAndChild> CreateAdminAndChild() {
        // the treasury only funds a wallet when a test needs a positive balance
        var treasury = await WalletDom.Create(TestInit1);
        var adminId = await CreateWallet();
        var childId = await CreateWallet();
        await SetMinBalance(adminId, treasury.CurrencyId, -1000);
        await SetMinBalance(childId, treasury.CurrencyId, -500);
        return new AdminAndChild(treasury, adminId, childId);
    }

    private sealed record AdminAndChild(WalletDom Treasury, int AdminId, int ChildId)
    {
        public int CurrencyId => Treasury.CurrencyId;
    }
}
