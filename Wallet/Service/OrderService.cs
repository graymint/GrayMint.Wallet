using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Exceptions;
using EWallet.Models;
using EWallet.Repo;

namespace EWallet.Service;

public class OrderService
{
    private readonly WalletRepo _walletRepo;
    private readonly AppService _appService;
    private List<WalletBalanceModel> _walletBalances = new();
    private long _newWalletTransactionId = -1;

    public OrderService(WalletRepo walletRepo, AppService appService)
    {
        _walletRepo = walletRepo;
        _appService = appService;
    }

    public async Task<Order> Create(int appId, CreateOrderRequest request)
    {
        // create order
        var order = await CreateOrder(appId, request);

        // process order
        await ProcessOrder(order);

        // set output
        var createdOrder = await GetOrder(appId, request.OrderId);
        return createdOrder;
    }

    private async Task ProcessOrder(OrderModel order)
    {
        // add system wallet id to list walletIds
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);

        var walletIds = order.OrderItems.GetWalletIds();
        walletIds.Add((int)order.App.SystemWalletId);
        _walletBalances = await _walletRepo.GetWalletBalances(order.AppId, order.CurrencyId, walletIds);

        // todo tof
        List<OrderItemModel> items;
        if (order.TransactionType == TransactionType.Authorize)
        {
            items = new List<OrderItemModel>()
            {
                order.OrderItems.First()
            };
        }
        else
        {
            items = order.OrderItems.ToList();
        }

        foreach (var participantWallet in items)
        {
            // make receiver transaction
            var receiverWalletId = order.TransactionType == TransactionType.Sale ? participantWallet.ReceiverWalletId : (int)order.App.SystemWalletId;
            await Transfer(participantWallet.SenderWalletId, receiverWalletId, participantWallet.ReceiverWalletId,
                participantWallet.Amount, participantWallet.OrderItemId);
        }

        // todo tof : new wallet balance records 
        await _walletRepo.AddEntities(_walletBalances.Where(x => x.WalletBalanceId == 0).ToArray());
        order.ProcessTime = DateTime.UtcNow;

        await _walletRepo.SaveChangesAsync();

        // reset cache, next caller must be get from db again
        _newWalletTransactionId = -1;
    }

    private async Task Transfer(int senderWalletId, int receiverWalletId, int actualReceiverWalletId, decimal amount, long orderItemId)
    {
        // get new wallet transaction id
        _newWalletTransactionId = _newWalletTransactionId == -1 ? await BuildNewWalletTransactionId() : _newWalletTransactionId;

        // make sender transaction
        var senderWalletTransaction = CreateOrderTransaction(senderWalletId, actualReceiverWalletId, -amount,
            _newWalletTransactionId, null, orderItemId);
        await _walletRepo.AddEntity(senderWalletTransaction);

        // make receiver transaction
        var receiverWalletTransaction = CreateOrderTransaction(receiverWalletId, actualReceiverWalletId, amount,
            _newWalletTransactionId + 1, null, orderItemId);
        await _walletRepo.AddEntity(receiverWalletTransaction);

        _newWalletTransactionId += 2;
    }

    private WalletTransactionModel CreateOrderTransaction(int walletId, int receiverWalletId, decimal amount,
        long walletTransactionId, long? walletTransactionReferenceId, long orderItemId)
    {
        // get sender info
        var walletBalance = _walletBalances.SingleOrDefault(x => x.WalletId == walletId);
        decimal newBalance = 0;

        if (amount < 0)
        {
            if (walletBalance is null ||
                (walletBalance.Balance < -amount && walletBalance.Balance + (-walletBalance.MinBalance) < -amount))
                throw new InsufficientBalanceException(
                    $"WalletId: {walletId} does not have sufficient balance, amount is: {amount}");

            if (walletBalance.Balance >= -amount)
            {
                newBalance = walletBalance.Balance - (-amount);

                // update cache
                _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance = walletBalance.Balance + amount;
            }

            else
            {
                newBalance = 0;

                // update cache
                _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance = 0;

                _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .MinBalance = walletBalance.MinBalance + (-amount);

                _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .ModifiedTime = DateTime.UtcNow;
            }
        }
        else if (amount > 0)
        {
            // update cache
            if (walletBalance is null)
            {
                newBalance = amount;

                _walletBalances.Add(new WalletBalanceModel
                {
                    WalletId = walletId,
                    CurrencyId = _walletBalances.First().CurrencyId,
                    Balance = amount,
                    MinBalance = 0,
                    ModifiedTime = DateTime.UtcNow
                });
            }
            else
            {
                newBalance = _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance + amount;

                _walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance = walletBalance.Balance + amount;
            }
        }

        // make sender transaction
        var walletTransactionModel = new WalletTransactionModel
        {
            WalletId = walletId,
            Amount = amount,
            Balance = newBalance,
            CreatedTime = DateTime.UtcNow,
            OrderItemId = orderItemId,
            ReceiverWalletId = receiverWalletId,
            WalletTransactionId = walletTransactionId,
            WalletTransactionReferenceId = walletTransactionReferenceId
        };

        return walletTransactionModel;
    }

    private async Task<OrderModel> CreateOrder(int appId, CreateOrderRequest request)
    {
        // get requested wallets models
        var walletIds = request.ParticipantWallets.GetWalletIds();
        var wallets = await _walletRepo.GetWallets(appId, walletIds);

        // Validate order request
        await ValidateCreateOrderRequest(appId, request, wallets);

        await _walletRepo.BeginTransaction();

        // create order
        var order = new OrderModel
        {
            OrderReferenceNumber = request.OrderId,
            CurrencyId = request.CurrencyId,
            AppId = appId,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            TransactionType = request.TransactionType,
            AuthorizedTime = request.TransactionType == TransactionType.Authorize ? DateTime.UtcNow : null,
            CapturedTime = request.TransactionType == TransactionType.Sale ? DateTime.UtcNow : null,
            ProcessTime = null
        };
        await _walletRepo.AddEntity(order);
        await _walletRepo.SaveChangesAsync();

        // create order Item
        await _walletRepo.AddEntities(request.ParticipantWallets.Select(x => new OrderItemModel
        {
            SenderWalletId = x.SenderWalletId,
            ReceiverWalletId = x.ReceiverWalletId,
            OrderId = order.OrderId,
            Amount = x.Amount
        }).ToArray());
        await _walletRepo.SaveChangesAsync();
        await _walletRepo.CommitTransaction();

        return await GetOrderModel(appId, request.OrderId);
    }

    private async Task<long> BuildNewWalletTransactionId()
    {
        var maxId = await _walletRepo.GetMaxWalletTransactionId();
        maxId = maxId is null ? 0 : maxId + 1;
        return (long)maxId;
    }

    private async Task ValidateCreateOrderRequest(int appId, CreateOrderRequest request, WalletModel[] requestedWallets)
    {
        // validate transaction type
        if (request.TransactionType != TransactionType.Authorize && request.TransactionType != TransactionType.Sale)
            throw new InvalidOperationException($"{request.TransactionType} does not accepted.");

        // Validate currency
        await _walletRepo.GetCurrency(appId, request.CurrencyId);

        // Validate amount
        if (request.ParticipantWallets.Any(item => item.Amount <= 0))
        {
            throw new InvalidOperationException("Amounts must be positive.");
        }

        if (request.ParticipantWallets.Any(item => item.SenderWalletId == item.ReceiverWalletId))
        {
            throw new InvalidOperationException("SenderWallet and ReceiverWallet can not be same.");
        }

        // get wallets based on request
        var requestedWalletIds = request.ParticipantWallets.GetWalletIds();

        var diffWalletIds = requestedWalletIds.Except(requestedWallets.Select(x => x.WalletId)).ToArray();
        if (diffWalletIds.Any())
            throw new InvalidOperationException($"some wallets does not belong to the app, walletIds are: {string.Join(",", diffWalletIds)}");

        // validate system wallet in order participants
        var app = await _appService.GetModel(appId);
        if (request.ParticipantWallets.SingleOrDefault(x =>
                x.ReceiverWalletId == app.SystemWalletId || x.SenderWalletId == app.SystemWalletId) is not null)
            throw new InvalidOperationException("system wallet can not participant in order.");

        // validate for duplicate items
        var duplicateItems = request.ParticipantWallets
            .GroupBy(x => new { x.ReceiverWalletId, x.SenderWalletId, x.Amount })
            .Where(x => x.Count() > 1)
            .Select(x => 1)
            .ToList();
        if (duplicateItems.Count > 0)
            throw new InvalidOperationException("duplicate records.");
    }

    public async Task<Order> Capture(int appId, Guid orderId)
    {
        // get order info
        var order = await GetOrderFull(appId, orderId);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        var orderStatus = order.ToDto().Status;

        if (orderStatus != OrderStatus.Pending)
            throw new InvalidTransactionTypeException("Capture process works only on authorize status.");

        // lock application
        // fill wallet balances cache
        var walletIds = order.OrderItems.GetWalletIds();
        walletIds.Add((int)order.App.SystemWalletId);
        _walletBalances = await _walletRepo.GetWalletBalances(order.AppId, order.CurrencyId, walletIds);

        foreach (var item in order.OrderItems)
        {
            // find out which wallet is receiver
            var senderWalletId = item.OrderTransactions is not null
                ? (int)order.App.SystemWalletId
                : item.SenderWalletId;
            await Transfer(senderWalletId, item.ReceiverWalletId, item.ReceiverWalletId, item.Amount, item.OrderItemId);
        }

        // todo tof : new wallet balance records 
        await _walletRepo.AddEntities(_walletBalances.Where(x => x.WalletBalanceId == 0).ToArray());

        // update order
        order.ModifiedTime = DateTime.UtcNow;
        order.CapturedTime = DateTime.UtcNow;

        await _walletRepo.SaveChangesAsync();

        // clean cache wallet balances
        _walletBalances = new List<WalletBalanceModel>();

        return await GetOrder(appId, orderId);
    }

    public async Task<Order> Void(int appId, Guid orderId)
    {
        // get order info
        var order = await GetOrderFull(appId, orderId);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        var orderStatus = order.ToDto().Status;

        if (order.VoidedTime is not null)
            throw new InvalidTransactionTypeException("Order is already Voided.");

        // lock application
        // fill wallet balances cache
        var walletIds = order.OrderItems.GetWalletIds();
        walletIds.Add((int)order.App.SystemWalletId);
        _walletBalances = await _walletRepo.GetWalletBalances(order.AppId, order.CurrencyId, walletIds);

        foreach (var item in order.OrderItems.Where(x => x.OrderTransactions is not null))
        {
            // find out which wallet is receiver
            var senderWalletId = orderStatus == OrderStatus.Pending
                ? (int)order.App.SystemWalletId
                : item.ReceiverWalletId;
            await Transfer(senderWalletId, item.SenderWalletId, item.SenderWalletId, item.Amount, item.OrderItemId);
        }

        // todo tof : new wallet balance records 
        await _walletRepo.AddEntities(_walletBalances.Where(x => x.WalletBalanceId == 0).ToArray());

        // update order
        order.ModifiedTime = DateTime.UtcNow;
        order.VoidedTime = DateTime.UtcNow;

        await _walletRepo.SaveChangesAsync();

        // clean cache wallet balances
        _walletBalances = new List<WalletBalanceModel>();

        return await GetOrder(appId, orderId);


        throw new NotImplementedException();
    }

    public async Task<Order> GetOrder(int appId, Guid orderId)
    {
        // Get order
        var order = await GetOrderModel(appId, orderId);
        return order.ToDto();
    }

    private async Task<OrderModel> GetOrderModel(int appId, Guid orderId)
    {
        // Get order
        var order = await _walletRepo.GetOrder(appId, orderId);
        return order;
    }

    private async Task<OrderModel> GetOrderFull(int appId, Guid orderId)
    {
        // Get order
        var order = await _walletRepo.GetOrderFull(appId, orderId);
        return order;
    }
}