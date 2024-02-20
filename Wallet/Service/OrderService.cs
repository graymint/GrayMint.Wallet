using EWallet.DtoConverters;
using EWallet.Dtos;
using EWallet.Exceptions;
using EWallet.Models;
using EWallet.Repo;

namespace EWallet.Service;

public class OrderService(WalletRepo walletRepo, AppService appService)
{
    public async Task<Order> Create(int appId, CreateOrderRequest request)
    {
        var idempotentOrder = await ValidateOrderIdempotent(appId, request);
        if (idempotentOrder is not null)
            return idempotentOrder.ToDto();

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

        // pre calculate balances
        await PreCalculateOrderItems(order);
        var walletTransferItems = new List<WalletTransferItem>();
        foreach (var participantWallet in order.OrderItems.ToList())
        {
            // make receiver transaction
            var receiverWalletId = order.TransactionType == TransactionType.Sale
                ? participantWallet.ReceiverWalletId
                : order.App.SystemWalletId.Value;

            walletTransferItems.Add(new WalletTransferItem
            {
                ParticipantTransferItem = new ParticipantTransferItem
                {
                    SenderWalletId = participantWallet.SenderWalletId,
                    ReceiverWalletId = receiverWalletId,
                    Amount = participantWallet.Amount
                },
                TransactionType = order.TransactionType,
                ActualReceiverWalletId = participantWallet.ReceiverWalletId,
                OrderItemId = participantWallet.OrderItemId
            });
        }
        await Transfers(order.App, order.CurrencyId, walletTransferItems);

        order.ProcessTime = DateTime.UtcNow;

        await walletRepo.SaveChangesAsync();
    }

    private void PreCalculateOrderItemReceiver(List<WalletBalanceModel> walletBalances, WalletBalanceModel? receiverWalletBalance,
        int receiverWalletId, decimal amount)
    {
        if (receiverWalletBalance is null)
        {
            walletBalances.Add(new WalletBalanceModel
            {
                WalletId = receiverWalletId,
                CurrencyId = walletBalances.First().CurrencyId,
                Balance = amount,
                MinBalance = 0,
                ModifiedTime = DateTime.UtcNow
            });
        }
        else
        {
            walletBalances.Single(x => x.WalletBalanceId == receiverWalletBalance.WalletBalanceId)
                .Balance = receiverWalletBalance.Balance + amount;
        }
    }

    private List<WalletBalanceModel> PreCalculateOrderItemSender(List<WalletBalanceModel> walletBalances, WalletBalanceModel? senderWalletBalance,
        int senderWalletId, decimal amount)
    {
        if (senderWalletBalance is null ||
            (senderWalletBalance.Balance < amount && senderWalletBalance.Balance + (-senderWalletBalance.MinBalance) < amount))
            throw new InsufficientBalanceException(
                $"WalletId: {senderWalletId} does not have sufficient balance, amount is: {amount}");

        if (senderWalletBalance.Balance >= amount)
        {
            // update temporary balance
            walletBalances.Single(x => x.WalletBalanceId == senderWalletBalance.WalletBalanceId)
                .Balance = senderWalletBalance.Balance - amount;
        }
        else
        {
            // update temporary balance
            walletBalances.Single(x => x.WalletBalanceId == senderWalletBalance.WalletBalanceId)
                .Balance = 0;

            walletBalances.Single(x => x.WalletBalanceId == senderWalletBalance.WalletBalanceId)
                .MinBalance = senderWalletBalance.MinBalance - (amount + senderWalletBalance.Balance);
        }

        return walletBalances;
    }

    private async Task PreCalculateOrderItems(OrderModel order)
    {
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);

        var walletIds = order.OrderItems.GetWalletIds();
        var walletBalances = await walletRepo.GetWalletBalancesWithoutTrack(order.AppId, order.CurrencyId, walletIds);
        foreach (var item in order.OrderItems)
        {
            PreCalculateOrderItem(walletBalances, item);
        }
    }

    private void PreCalculateOrderItem(List<WalletBalanceModel> walletBalances, OrderItemModel item)
    {
        var senderWalletBalance = walletBalances.SingleOrDefault(x => x.WalletId == item.SenderWalletId);
        walletBalances = PreCalculateOrderItemSender(walletBalances, senderWalletBalance, item.SenderWalletId, item.Amount);

        var receiverWalletBalance = walletBalances.SingleOrDefault(x => x.WalletId == item.ReceiverWalletId);
        PreCalculateOrderItemReceiver(walletBalances, receiverWalletBalance, item.ReceiverWalletId, item.Amount);
    }

    private async Task Transfers(AppModel app, int currencyId, List<WalletTransferItem> items)
    {
        ArgumentNullException.ThrowIfNull(app.SystemWalletId);
        var newWalletTransactionId = await BuildNewWalletTransactionId();
        var walletIds = GetWalletIds(items);
        walletIds.Add(app.SystemWalletId.Value);
        var walletBalances = await walletRepo.GetWalletBalances(app.AppId, currencyId, walletIds);

        foreach (var item in items)
        {
            await Transfer(item.ParticipantTransferItem.SenderWalletId, item.ParticipantTransferItem.ReceiverWalletId,
                item.ActualReceiverWalletId, item.ParticipantTransferItem.Amount, item.OrderItemId, newWalletTransactionId,
                walletBalances, item.TransactionType);

            newWalletTransactionId += 2;
        }
        await walletRepo.AddEntities(walletBalances.Where(x => x.WalletBalanceId == 0).ToArray());
    }

    private List<int> GetWalletIds(ICollection<WalletTransferItem> items)
    {       // get list senders
        var list = items.Select(x => x.ParticipantTransferItem.SenderWalletId)
            .Distinct()
            .ToList();

        // add receivers to list
        list.AddRange(items.Select(x => x.ParticipantTransferItem.ReceiverWalletId)
            .Distinct()
            .ToList());

        return list
            .Distinct()
            .ToList();

    }

    private async Task Transfer(int senderWalletId, int receiverWalletId, int actualReceiverWalletId, decimal amount, long orderItemId,
        long walletTransactionId, List<WalletBalanceModel> walletBalances, TransactionType? transactionType = null)
    {
        // make sender transaction
        var senderWalletTransaction = CreateOrderTransaction(senderWalletId, actualReceiverWalletId, -amount,
            walletTransactionId, null, orderItemId,
            walletBalances: walletBalances, transactionType: transactionType);
        await walletRepo.AddEntity(senderWalletTransaction);

        // make receiver transaction
        var receiverWalletTransaction = CreateOrderTransaction(receiverWalletId, actualReceiverWalletId, amount,
            walletTransactionId + 1, null, orderItemId,
            walletBalances: walletBalances, transactionType: transactionType);
        await walletRepo.AddEntity(receiverWalletTransaction);
    }

    private WalletTransactionModel CreateOrderTransaction(int walletId, int receiverWalletId, decimal amount,
        long walletTransactionId, long? walletTransactionReferenceId, long orderItemId,
        List<WalletBalanceModel> walletBalances, TransactionType? transactionType = null)
    {
        // get sender info
        var walletBalance = walletBalances.SingleOrDefault(x => x.WalletId == walletId);

        decimal newBalance = 0;

        switch (amount)
        {
            case < 0:
                {
                    if (walletBalance is not null && walletBalance.Balance >= -amount)
                    {
                        newBalance = walletBalance.Balance - (-amount);

                        // update cache
                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .Balance = walletBalance.Balance + amount;
                        break;
                    }

                    if (walletBalance is not null && walletBalance.Balance + (-walletBalance.MinBalance) >= -amount)
                    {
                        newBalance = 0;

                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .MinBalance = walletBalance.MinBalance - (walletBalance.Balance + amount);

                        // update cache
                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .Balance = 0;

                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .ModifiedTime = DateTime.UtcNow;
                        break;
                    }

                    if (transactionType == TransactionType.Authorize)
                    {
                        if (walletBalance is null)
                        {
                            newBalance = amount;
                            walletBalances.Add(new WalletBalanceModel
                            {
                                WalletId = walletId,
                                CurrencyId = walletBalances.First().CurrencyId,
                                MinBalance = 0,
                                Balance = amount,
                                ModifiedTime = DateTime.UtcNow
                            });
                            break;
                        }

                        // update cache
                        var currentBalance = walletBalance.Balance;
                        var currentMinBalance = walletBalance.MinBalance;

                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .Balance = (amount) + (currentBalance + (-currentMinBalance));

                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .MinBalance = 0;

                        walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                            .ModifiedTime = DateTime.UtcNow;
                        break;
                    }

                    throw new ArgumentOutOfRangeException(nameof(amount), amount, "Balance can not calculate.");
                }
            // update cache
            case > 0 when walletBalance is null:
                newBalance = amount;

                walletBalances.Add(new WalletBalanceModel
                {
                    WalletId = walletId,
                    CurrencyId = walletBalances.First().CurrencyId,
                    Balance = amount,
                    MinBalance = 0,
                    ModifiedTime = DateTime.UtcNow
                });
                break;
            case > 0:
                newBalance = walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance + amount;

                walletBalances.Single(x => x.WalletBalanceId == walletBalance.WalletBalanceId)
                    .Balance = walletBalance.Balance + amount;
                break;
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
        var wallets = await walletRepo.GetWallets(appId, walletIds);

        // Validate order request
        await ValidateCreateOrderRequest(appId, request, wallets);

        await walletRepo.BeginTransaction();

        // create order
        var order = new OrderModel
        {
            OrderReferenceNumber = request.OrderId,
            CurrencyId = request.CurrencyId,
            OrderTypeId = request.OrderTypeId,
            AppId = appId,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            TransactionType = request.TransactionType,
            AuthorizedTime = request.TransactionType == TransactionType.Authorize ? DateTime.UtcNow : null,
            CapturedTime = request.TransactionType == TransactionType.Sale ? DateTime.UtcNow : null,
            ProcessTime = null
        };
        await walletRepo.AddEntity(order);
        await walletRepo.SaveChangesAsync();

        // create order Item
        await walletRepo.AddEntities(request.ParticipantWallets.Select(x => new OrderItemModel
        {
            SenderWalletId = x.SenderWalletId,
            ReceiverWalletId = x.ReceiverWalletId,
            OrderId = order.OrderId,
            Amount = x.Amount
        }).ToArray());
        await walletRepo.SaveChangesAsync();
        await walletRepo.CommitTransaction();

        return await GetOrderModel(appId, request.OrderId);
    }

    private async Task<long> BuildNewWalletTransactionId()
    {
        var maxId = await walletRepo.GetMaxWalletTransactionId();
        maxId = maxId is null ? 0 : maxId + 1;
        return (long)maxId;
    }

    private async Task ValidateCreateOrderRequest(int appId, CreateOrderRequest request, WalletModel[] requestedWallets)
    {
        // validate transaction type
        if (request.TransactionType != TransactionType.Authorize && request.TransactionType != TransactionType.Sale)
            throw new InvalidOperationException($"{request.TransactionType} does not accepted.");

        // Validate currency
        await walletRepo.GetCurrency(appId, request.CurrencyId);

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
        var app = await appService.GetModel(appId);
        if (request.ParticipantWallets.SingleOrDefault(x =>
                x.ReceiverWalletId == app.SystemWalletId || x.SenderWalletId == app.SystemWalletId) is not null)
            throw new InvalidOperationException("system wallet can not participant in order.");

        // validate for duplicate items
        var duplicateItems = request.ParticipantWallets
            .GroupBy(x => new { x.ReceiverWalletId, x.SenderWalletId, x.Amount })
            .Where(x => x.Count() > 1)
            .Select(_ => 1)
            .ToList();
        if (duplicateItems.Count > 0)
            throw new InvalidOperationException("duplicate records.");
    }

    private async Task<OrderModel?> ValidateOrderIdempotent(int appId, CreateOrderRequest request)
    {
        var order = await walletRepo.FindOrder(appId, request.OrderId);
        if (order is null)
            return null;

        ArgumentNullException.ThrowIfNull(order.OrderItems);

        var requestParticipants = request.ParticipantWallets
            .Select(x => new { x.SenderWalletId, x.ReceiverWalletId })
            .ToArray();

        var orderParticipants = order.OrderItems
            .Select(x => new { x.SenderWalletId, x.ReceiverWalletId }).ToArray();

        if (order.CurrencyId == request.CurrencyId &&
               order.OrderTypeId == request.OrderTypeId &&
               order.TransactionType == request.TransactionType &&
                orderParticipants.SequenceEqual(requestParticipants))
            return order;
        return null;
    }

    public async Task<Order> Capture(int appId, Guid orderId)
    {
        // todo: lock application
        // get order info
        var order = await GetOrderFull(appId, orderId);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        var orderStatus = order.ToDto().Status;

        if (orderStatus != OrderStatus.Authorized)
            throw new InvalidTransactionTypeException("Capture process works only on authorize status.");

        // fill wallet balances cache
        var walletIds = order.OrderItems.GetWalletIds();
        walletIds.Add(order.App.SystemWalletId.Value);

        var walletTransferItems = new List<WalletTransferItem>();
        foreach (var item in order.OrderItems)
        {
            // find out which wallet is receiver
            var senderWalletId = order.App.SystemWalletId.Value;
            walletTransferItems.Add(new WalletTransferItem
            {
                ParticipantTransferItem = new ParticipantTransferItem
                {
                    SenderWalletId = senderWalletId,
                    ReceiverWalletId = item.ReceiverWalletId,
                    Amount = item.Amount
                },
                TransactionType = null,
                ActualReceiverWalletId = item.ReceiverWalletId,
                OrderItemId = item.OrderItemId
            });
        }
        await Transfers(order.App, order.CurrencyId, walletTransferItems);

        // todo new wallet balance records 

        // update order
        order.ModifiedTime = DateTime.UtcNow;
        order.CapturedTime = DateTime.UtcNow;

        await walletRepo.SaveChangesAsync();
        return await GetOrder(appId, orderId);
    }

    public async Task<Order> Void(int appId, Guid orderId)
    {
        await walletRepo.BeginTransaction();

        // get order info
        var order = await GetOrderFull(appId, orderId);
        ArgumentNullException.ThrowIfNull(order.App);
        ArgumentNullException.ThrowIfNull(order.App.SystemWalletId);
        ArgumentNullException.ThrowIfNull(order.OrderItems);
        var orderStatus = order.ToDto().Status;

        if (order.VoidedTime is not null)
            throw new InvalidTransactionTypeException("Order is already Voided.");

        // fill wallet balances cache
        var walletIds = order.OrderItems.GetWalletIds();
        walletIds.Add(order.App.SystemWalletId.Value);

        var walletTransferItems = new List<WalletTransferItem>();
        foreach (var item in order.OrderItems.Where(x => x.OrderTransactions is not null))
        {
            // find out which wallet is receiver
            var senderWalletId = orderStatus == OrderStatus.Authorized
                ? order.App.SystemWalletId.Value
                : item.ReceiverWalletId;
            walletTransferItems.Add(new WalletTransferItem
            {
                ParticipantTransferItem = new ParticipantTransferItem
                {
                    SenderWalletId = senderWalletId,
                    ReceiverWalletId = item.SenderWalletId,
                    Amount = item.Amount
                },
                TransactionType = null,
                ActualReceiverWalletId = item.SenderWalletId,
                OrderItemId = item.OrderItemId
            });
        }
        await Transfers(order.App, order.CurrencyId, walletTransferItems);

        // update order
        order.ModifiedTime = DateTime.UtcNow;
        order.VoidedTime = DateTime.UtcNow;

        await walletRepo.SaveChangesAsync();
        await walletRepo.CommitTransaction();

        // clean cache wallet balances
        return await GetOrder(order.AppId, order.OrderReferenceNumber);
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
        var order = await walletRepo.GetOrder(appId, orderId);
        return order;
    }

    private async Task<OrderModel> GetOrderFull(int appId, Guid orderId)
    {
        // Get order
        var order = await walletRepo.GetOrderFull(appId, orderId);
        return order;
    }
}