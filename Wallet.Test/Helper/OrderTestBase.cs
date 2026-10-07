using EWallet.Api;

namespace EWallet.Test.Helper;

public abstract class OrderTestBase : BaseControllerTest
{
    protected async Task<int> CreateWallet() {
        var wallet = await TestInit1.WalletsClient.CreateWalletAsync(TestInit1.AppId);
        return wallet.WalletId;
    }

    protected Task<Order> CreateOrder(int senderWalletId, int receiverWalletId, int currencyId, decimal amount,
        bool? allowDebt, TransactionType transactionType = TransactionType.Sale, Guid? orderId = null) {
        return TestInit1.OrdersClient.CreateOrderAsync(TestInit1.AppId, new CreateOrderRequest
        {
            OrderId = orderId ?? Guid.NewGuid(),
            CurrencyId = currencyId,
            OrderTypeId = 1,
            TransactionType = transactionType,
            AllowDebt = allowDebt,
            ParticipantWallets =
            [
                new ParticipantTransferItem
                {
                    SenderWalletId = senderWalletId,
                    ReceiverWalletId = receiverWalletId,
                    Amount = amount
                }
            ]
        });
    }

    protected Task<Order> CreateOrder(int currencyId, bool? allowDebt, params ParticipantTransferItem[] legs) {
        return TestInit1.OrdersClient.CreateOrderAsync(TestInit1.AppId, new CreateOrderRequest
        {
            OrderId = Guid.NewGuid(),
            CurrencyId = currencyId,
            OrderTypeId = 1,
            TransactionType = TransactionType.Sale,
            AllowDebt = allowDebt,
            ParticipantWallets = legs
        });
    }

    protected static ParticipantTransferItem Leg(int senderWalletId, int receiverWalletId, decimal amount) {
        return new ParticipantTransferItem
        {
            SenderWalletId = senderWalletId,
            ReceiverWalletId = receiverWalletId,
            Amount = amount
        };
    }

    protected Task SetMinBalance(int walletId, int currencyId, decimal minBalance) {
        return TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, walletId, new SetMinBalanceRequest
        {
            CurrencyId = currencyId,
            MinBalance = minBalance
        });
    }

    protected static CreateSingleTransferRequest CreateTransfer(int senderWalletId, int receiverWalletId, int currencyId,
        decimal amount, bool? allowDebt) {
        return new CreateSingleTransferRequest
        {
            OrderId = Guid.NewGuid(),
            SenderWalletId = senderWalletId,
            ReceiverWalletId = receiverWalletId,
            CurrencyId = currencyId,
            Amount = amount,
            OrderTypeId = 1,
            TransactionType = TransactionType.Sale,
            AllowDebt = allowDebt
        };
    }

    protected async Task<CurrencyBalance?> GetBalance(int walletId, int currencyId) {
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletId);
        return wallet.Currencies?.SingleOrDefault(x => x.CurrencyId == currencyId);
    }

    protected async Task AssertBalance(int walletId, int currencyId, decimal balance, decimal minBalance) {
        var walletBalance = await GetBalance(walletId, currencyId);
        Assert.IsNotNull(walletBalance, $"WalletId {walletId} has no balance.");
        Assert.AreEqual(balance, walletBalance.Balance, $"Balance of WalletId {walletId}");
        Assert.AreEqual(minBalance, walletBalance.MinBalance, $"MinBalance of WalletId {walletId}");
    }
}
