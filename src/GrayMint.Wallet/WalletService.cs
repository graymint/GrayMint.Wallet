using GrayMint.Common.Exceptions;
using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Exceptions;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet;

// The ledger's one way in. A call that writes runs in the transaction that is open, which is the host's when the
// wallet works on the host's connection, so the host's rows and the money commit together; with none open, it runs
// in one of its own. The wallet's context is its own, so a call never saves what the host has not saved.
public class WalletService
{
    internal const int MaxOwnerIdLength = 100;
    internal const int MaxCodeLength = 50;
    internal const int MaxCurrencyLength = 10;
    internal const int MaxKeyLength = 100;
    internal const int AmountScale = 8;
    internal const int MaxChainLength = 100;
    internal const int MaxPageSize = 100;

    // One transfer's most. An account's totals only grow, up to the columns' 20 whole digits, as many as a decimal
    // holds with 8 places: the database refuses a transfer that would take a total past them.
    internal const decimal MaxAmount = 1_000_000_000_000_000m;

    private readonly WalletDbContext _walletDbContext;
    private readonly HostDatabase? _hostDatabase;
    private readonly WalletDialect _dialect;

    internal WalletService(WalletDbContext walletDbContext, HostDatabase? hostDatabase)
    {
        _walletDbContext = walletDbContext;
        _hostDatabase = hostDatabase;
        _dialect = WalletDialect.For(walletDbContext.Database);
    }

    public async Task<WalletAccount> CreateAccount(int ledgerId, WalletAccountCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalidText(request.OwnerId, MaxOwnerIdLength, nameof(request.OwnerId));
        ThrowIfInvalidText(request.Code, MaxCodeLength, nameof(request.Code));
        ThrowIfInvalidCurrency(request.Currency);
        if (request.MinBalance != null) {
            ThrowIfInvalidLimit(request.MinBalance.Value, nameof(request.MinBalance));
            if (request.MinBalance > 0)
                throw new ArgumentException("A new account has nothing, so its MinBalance can't be above 0.",
                    nameof(request));
        }

        var account = new WalletAccount {
            LedgerId = ledgerId,
            OwnerId = request.OwnerId,
            Code = request.Code,
            Currency = request.Currency,
            MinBalance = request.MinBalance
        };
        var conflict = new WalletConflictException(
            $"Ledger {ledgerId} has an account {request.Code} in {request.Currency} for {request.OwnerId} already.");

        await using var session = await BeginSession(isWrite: true, cancellationToken);
        if (await HasAccount(account, cancellationToken))
            throw conflict;

        // the same account made by another call meanwhile
        _walletDbContext.Accounts.Add(account);
        await Save(() => HasAccount(account, cancellationToken), conflict, cancellationToken);
        await session.Commit(cancellationToken);
        return account;
    }

    public async Task<WalletAccount> GetAccount(int ledgerId, long accountId, CancellationToken cancellationToken)
    {
        return await FindAccount(ledgerId, accountId, cancellationToken)
               ?? throw new NotExistsException($"Account {accountId} is not in ledger {ledgerId}.");
    }

    public async Task<WalletAccount?> FindAccount(int ledgerId, string ownerId, string code, string currency,
        CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.OwnerId == ownerId && x.Code == code &&
                                       x.Currency == currency, cancellationToken);
    }

    public async Task<IReadOnlyList<WalletAccount>> GetOwnerAccounts(int ledgerId, string ownerId,
        CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Accounts
            .AsNoTracking()
            .Where(x => x.LedgerId == ledgerId && x.OwnerId == ownerId)
            .OrderBy(x => x.AccountId)
            .ToArrayAsync(cancellationToken);
    }

    // A lower limit always takes; a higher one only while the account still keeps to it.
    public async Task<WalletAccount> SetMinBalance(int ledgerId, long accountId, decimal? minBalance,
        CancellationToken cancellationToken)
    {
        if (minBalance != null)
            ThrowIfInvalidLimit(minBalance.Value, nameof(minBalance));

        await using (var session = await BeginSession(isWrite: true, cancellationToken)) {
            var account = (await LockAccounts(ledgerId, [accountId], cancellationToken)).Single();
            if (minBalance != null && account.Available < minBalance)
                throw new WalletConflictException(
                    $"Account {accountId} has {account.Available} to spend, below the {minBalance} it would have to keep.");

            await _walletDbContext.Accounts
                .Where(x => x.AccountId == accountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.MinBalance, minBalance), cancellationToken);
            await session.Commit(cancellationToken);
        }

        return await GetAccount(ledgerId, accountId, cancellationToken);
    }

    // The transfers are a chain: each sees the ones before it, and all are made or none is. Sent again with the
    // same keys, the chain changes nothing and its transfers come back as they were made.
    public async Task<IReadOnlyList<WalletTransfer>> CreateTransfers(int ledgerId,
        IReadOnlyList<WalletTransferRequest> requests, CancellationToken cancellationToken)
    {
        ThrowIfInvalidChain(requests);

        await using var session = await BeginSession(isWrite: true, cancellationToken);
        var accountIds = requests.SelectMany(x => new[] { x.DebitAccountId, x.CreditAccountId }).ToArray();
        var accounts = await LockAccounts(ledgerId, accountIds, cancellationToken);

        // after the lock: a chain sent twice at once waits here for the first, then finds its transfers
        var keys = requests.Select(x => x.Key).ToArray();
        var madeBefore = await _walletDbContext.Transfers
            .AsNoTracking()
            .Where(x => x.LedgerId == ledgerId && x.Key != null && keys.Contains(x.Key))
            .ToArrayAsync(cancellationToken);
        if (madeBefore.Length > 0)
            return GetChainMadeBefore(ledgerId, requests, madeBefore);

        var changes = accounts.ToDictionary(x => x.AccountId, x => new AccountChange(x));
        var transfers = new List<WalletTransfer>();
        foreach (var request in requests) {
            var debit = changes[request.DebitAccountId];
            var credit = changes[request.CreditAccountId];
            if (debit.Account.Currency != credit.Account.Currency)
                throw new ArgumentException(
                    $"Transfer {request.Key} moves money from {debit.Account.Currency} to {credit.Account.Currency}.",
                    nameof(requests));

            debit.Debit(request.Amount, request.IsPending);
            credit.Credit(request.Amount, request.IsPending);
            transfers.Add(new WalletTransfer {
                LedgerId = ledgerId,
                Currency = debit.Account.Currency,
                Type = request.IsPending ? WalletTransferType.Pending : WalletTransferType.Posted,
                Key = request.Key,
                DebitAccountId = request.DebitAccountId,
                CreditAccountId = request.CreditAccountId,
                Amount = request.Amount,
                Code = request.Code
            });
        }

        await InsertTransfers(transfers, cancellationToken);
        await UpdateTotals(changes.Values, cancellationToken);
        await session.Commit(cancellationToken);
        return transfers;
    }

    public Task<IReadOnlyList<WalletTransfer>> PostPendingTransfers(int ledgerId, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        return SettlePendingTransfers(ledgerId, keys, WalletTransferType.PostPending, cancellationToken);
    }

    public Task<IReadOnlyList<WalletTransfer>> VoidPendingTransfers(int ledgerId, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        return SettlePendingTransfers(ledgerId, keys, WalletTransferType.VoidPending, cancellationToken);
    }

    public async Task<WalletTransfer> GetTransfer(int ledgerId, string key, CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Transfers
                   .AsNoTracking()
                   .SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Key == key, cancellationToken)
               ?? throw new NotExistsException($"No transfer has the key {key} in ledger {ledgerId}.");
    }

    // what a pending transfer came to, if it was posted or voided
    public async Task<WalletTransfer?> FindSettlement(int ledgerId, long pendingTransferId,
        CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.PendingTransferId == pendingTransferId,
                cancellationToken);
    }

    // newest first; beforeTransferId goes on from the last one of a page
    public async Task<IReadOnlyList<WalletTransfer>> GetAccountTransfers(int ledgerId, long accountId,
        long? beforeTransferId, int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"A page has 1 to {MaxPageSize} transfers.");

        await GetAccount(ledgerId, accountId, cancellationToken);
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Transfers
            .AsNoTracking()
            .Where(x => x.LedgerId == ledgerId &&
                        (x.DebitAccountId == accountId || x.CreditAccountId == accountId) &&
                        (beforeTransferId == null || x.TransferId < beforeTransferId))
            .OrderByDescending(x => x.TransferId)
            .Take(count)
            .ToArrayAsync(cancellationToken);
    }

    // Every account the caller can see whose stored totals are not what its transfers add up to: a write that went
    // round the service, or a fault in it. Meant for a job that runs it now and then and raises an alarm.
    // One query isn't sure to read an account's totals and its transfers at one moment (SQL Server, unless the
    // database reads committed rows by their versions), so it only names the accounts to look at again, each
    // under its lock, which no write of it gets past.
    public async Task<IReadOnlyList<WalletTotalsMismatch>> FindTotalsMismatches(CancellationToken cancellationToken)
    {
        var mismatches = new List<WalletTotalsMismatch>();
        foreach (var candidate in await FindTotalsMismatches(accountId: null, cancellationToken)) {
            await using var session = await BeginSession(isWrite: true, cancellationToken);
            await LockAccounts(candidate.LedgerId, [candidate.AccountId], cancellationToken);
            mismatches.AddRange(await FindTotalsMismatches(candidate.AccountId, cancellationToken));
        }

        return mismatches;
    }

    private async Task<IReadOnlyList<WalletTotalsMismatch>> FindTotalsMismatches(long? accountId,
        CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        var transfers = _walletDbContext.Transfers;
        var rows = await _walletDbContext.Accounts
            .AsNoTracking()
            .Where(x => accountId == null || x.AccountId == accountId)
            .Select(account => new {
                Account = account,
                FromTransfers = new WalletAccountTotals {
                    DebitsPending = transfers
                        .Where(x => x.DebitAccountId == account.AccountId)
                        .Sum(x => x.Type == WalletTransferType.Pending ? x.Amount
                            : x.Type == WalletTransferType.Posted ? 0m : -x.Amount),
                    DebitsPosted = transfers
                        .Where(x => x.DebitAccountId == account.AccountId)
                        .Sum(x => x.Type == WalletTransferType.Posted || x.Type == WalletTransferType.PostPending
                            ? x.Amount : 0m),
                    CreditsPending = transfers
                        .Where(x => x.CreditAccountId == account.AccountId)
                        .Sum(x => x.Type == WalletTransferType.Pending ? x.Amount
                            : x.Type == WalletTransferType.Posted ? 0m : -x.Amount),
                    CreditsPosted = transfers
                        .Where(x => x.CreditAccountId == account.AccountId)
                        .Sum(x => x.Type == WalletTransferType.Posted || x.Type == WalletTransferType.PostPending
                            ? x.Amount : 0m)
                }
            })
            .Where(x => x.Account.DebitsPending != x.FromTransfers.DebitsPending ||
                        x.Account.DebitsPosted != x.FromTransfers.DebitsPosted ||
                        x.Account.CreditsPending != x.FromTransfers.CreditsPending ||
                        x.Account.CreditsPosted != x.FromTransfers.CreditsPosted)
            .OrderBy(x => x.Account.AccountId)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new WalletTotalsMismatch {
                AccountId = x.Account.AccountId,
                LedgerId = x.Account.LedgerId,
                Stored = new WalletAccountTotals {
                    DebitsPending = x.Account.DebitsPending,
                    DebitsPosted = x.Account.DebitsPosted,
                    CreditsPending = x.Account.CreditsPending,
                    CreditsPosted = x.Account.CreditsPosted
                },
                FromTransfers = x.FromTransfers
            })
            .ToArray();
    }

    // Settled one by one: a pending transfer settled the same way before comes back as it was, so a list sent again
    // changes nothing; one settled the other way is refused.
    private async Task<IReadOnlyList<WalletTransfer>> SettlePendingTransfers(int ledgerId,
        IReadOnlyList<string> keys, WalletTransferType settlementType, CancellationToken cancellationToken)
    {
        ThrowIfInvalidKeys(keys);

        await using var session = await BeginSession(isWrite: true, cancellationToken);
        var pendingTransfers = await _walletDbContext.Transfers
            .AsNoTracking()
            .Where(x => x.LedgerId == ledgerId && x.Key != null && keys.Contains(x.Key))
            .ToArrayAsync(cancellationToken);
        foreach (var key in keys) {
            var pendingTransfer = pendingTransfers.SingleOrDefault(x => x.Key == key)
                                  ?? throw new NotExistsException($"No transfer has the key {key} in ledger {ledgerId}.");
            if (pendingTransfer.Type != WalletTransferType.Pending)
                throw new WalletConflictException($"Transfer {key} is no pending transfer: it was posted when it was made.");
        }

        var accountIds = pendingTransfers.SelectMany(x => new[] { x.DebitAccountId, x.CreditAccountId }).ToArray();
        var accounts = await LockAccounts(ledgerId, accountIds, cancellationToken);

        // after the lock, so a settlement made meanwhile is seen here
        var pendingTransferIds = pendingTransfers.Select(x => x.TransferId).ToArray();
        var settledBefore = await _walletDbContext.Transfers
            .AsNoTracking()
            .Where(x => x.LedgerId == ledgerId && x.PendingTransferId != null &&
                        pendingTransferIds.Contains(x.PendingTransferId.Value))
            .ToArrayAsync(cancellationToken);

        var changes = accounts.ToDictionary(x => x.AccountId, x => new AccountChange(x));
        var isPosted = settlementType == WalletTransferType.PostPending;
        var settlements = new List<WalletTransfer>();
        foreach (var key in keys) {
            var pendingTransfer = pendingTransfers.Single(x => x.Key == key);
            var settlement = settledBefore.SingleOrDefault(x => x.PendingTransferId == pendingTransfer.TransferId);
            if (settlement != null) {
                if (settlement.Type != settlementType)
                    throw new WalletConflictException(settlement.Type == WalletTransferType.PostPending
                        ? $"Pending transfer {key} was posted already."
                        : $"Pending transfer {key} was voided already.");
                continue;
            }

            changes[pendingTransfer.DebitAccountId].SettleDebit(pendingTransfer.Amount, isPosted);
            changes[pendingTransfer.CreditAccountId].SettleCredit(pendingTransfer.Amount, isPosted);
            settlements.Add(new WalletTransfer {
                LedgerId = ledgerId,
                Currency = pendingTransfer.Currency,
                Type = settlementType,
                DebitAccountId = pendingTransfer.DebitAccountId,
                CreditAccountId = pendingTransfer.CreditAccountId,
                Amount = pendingTransfer.Amount,
                Code = pendingTransfer.Code,
                PendingTransferId = pendingTransfer.TransferId
            });
        }

        await InsertTransfers(settlements, cancellationToken);
        await UpdateTotals(changes.Values, cancellationToken);
        await session.Commit(cancellationToken);

        // in the order of the keys, those settled before beside those settled now
        var all = settledBefore.Concat(settlements).ToArray();
        return keys
            .Select(key => pendingTransfers.Single(x => x.Key == key).TransferId)
            .Select(pendingTransferId => all.Single(x => x.PendingTransferId == pendingTransferId))
            .ToArray();
    }

    // Each transfer the keys name must be the one that request made, or the keys belong to another chain.
    private static IReadOnlyList<WalletTransfer> GetChainMadeBefore(int ledgerId,
        IReadOnlyList<WalletTransferRequest> requests, IReadOnlyList<WalletTransfer> madeBefore)
    {
        var chain = new List<WalletTransfer>();
        foreach (var request in requests) {
            var transfer = madeBefore.SingleOrDefault(x => x.Key == request.Key);
            if (transfer == null || !IsMadeBy(transfer, request))
                throw new WalletConflictException(
                    $"Ledger {ledgerId} has a transfer {madeBefore[0].Key} already, which is not part of this chain as sent.");
            chain.Add(transfer);
        }

        return chain;
    }

    private static bool IsMadeBy(WalletTransfer transfer, WalletTransferRequest request)
    {
        var type = request.IsPending ? WalletTransferType.Pending : WalletTransferType.Posted;
        return transfer.Type == type &&
               transfer.DebitAccountId == request.DebitAccountId &&
               transfer.CreditAccountId == request.CreditAccountId &&
               transfer.Amount == request.Amount &&
               transfer.Code == request.Code;
    }

    // One by one in id order, so two calls that need the same accounts never wait for each other in a circle, on
    // either database. The transaction holds the locks until it ends.
    private async Task<IReadOnlyList<WalletAccount>> LockAccounts(int ledgerId, IReadOnlyList<long> accountIds,
        CancellationToken cancellationToken)
    {
        var accounts = new List<WalletAccount>();
        foreach (var accountId in accountIds.Distinct().Order()) {
            var rows = await _walletDbContext.Accounts
                .FromSqlRaw(_dialect.LockAccountSql, ledgerId, accountId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            accounts.Add(rows.SingleOrDefault()
                         ?? throw new NotExistsException($"Account {accountId} is not in ledger {ledgerId}."));
        }

        return accounts;
    }

    // One at a time, so the ids follow the chain on either database.
    private async Task InsertTransfers(IReadOnlyList<WalletTransfer> transfers, CancellationToken cancellationToken)
    {
        foreach (var transfer in transfers) {
            // A key that another call took meanwhile without sharing an account with this one, so the lock didn't
            // keep them apart; or a pending transfer settled meanwhile.
            var conflict = new WalletConflictException(transfer.Key != null
                ? $"Ledger {transfer.LedgerId} has a transfer {transfer.Key} already."
                : $"Pending transfer {transfer.PendingTransferId} was settled meanwhile.");
            _walletDbContext.Transfers.Add(transfer);
            await Save(() => IsTransferTaken(transfer, cancellationToken), conflict, cancellationToken);
        }
    }

    // By the change, not to a value read before, so the database adds it to what it holds under the lock.
    private async Task UpdateTotals(IEnumerable<AccountChange> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes.Where(x => x.IsChanged)) {
            await _walletDbContext.Accounts
                .Where(x => x.AccountId == change.Account.AccountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.DebitsPending, x => x.DebitsPending + change.DebitsPending)
                    .SetProperty(x => x.DebitsPosted, x => x.DebitsPosted + change.DebitsPosted)
                    .SetProperty(x => x.CreditsPending, x => x.CreditsPending + change.CreditsPending)
                    .SetProperty(x => x.CreditsPosted, x => x.CreditsPosted + change.CreditsPosted),
                    cancellationToken);
        }
    }

    // EF takes back a refused save to a savepoint of its own, so the transaction stays usable to tell a conflict
    // from any other refusal, which goes on as it came.
    private async Task Save(Func<Task<bool>> isConflict, WalletConflictException conflict,
        CancellationToken cancellationToken)
    {
        try {
            await _walletDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) {
            _walletDbContext.ChangeTracker.Clear();
            if (!WalletSession.IsTransactionEnded(_walletDbContext.Database) && await isConflict())
                throw conflict;
            throw;
        }
        finally {
            _walletDbContext.ChangeTracker.Clear();
        }
    }

    private Task<bool> HasAccount(WalletAccount account, CancellationToken cancellationToken)
    {
        return _walletDbContext.Accounts.AnyAsync(x =>
            x.LedgerId == account.LedgerId && x.OwnerId == account.OwnerId && x.Code == account.Code &&
            x.Currency == account.Currency, cancellationToken);
    }

    private Task<bool> IsTransferTaken(WalletTransfer transfer, CancellationToken cancellationToken)
    {
        return transfer.Key != null
            ? _walletDbContext.Transfers.AnyAsync(x => x.LedgerId == transfer.LedgerId && x.Key == transfer.Key,
                cancellationToken)
            : _walletDbContext.Transfers.AnyAsync(x => x.PendingTransferId == transfer.PendingTransferId,
                cancellationToken);
    }

    private async Task<WalletAccount?> FindAccount(int ledgerId, long accountId, CancellationToken cancellationToken)
    {
        await using var session = await BeginSession(isWrite: false, cancellationToken);
        return await _walletDbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.AccountId == accountId, cancellationToken);
    }

    private Task<WalletSession> BeginSession(bool isWrite, CancellationToken cancellationToken)
    {
        return WalletSession.Begin(_walletDbContext, _hostDatabase, isWrite, cancellationToken);
    }

    private static void ThrowIfInvalidChain(IReadOnlyList<WalletTransferRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count is < 1 or > MaxChainLength)
            throw new ArgumentException($"A chain has 1 to {MaxChainLength} transfers.", nameof(requests));

        foreach (var request in requests) {
            ArgumentNullException.ThrowIfNull(request, nameof(requests));
            ThrowIfInvalidText(request.Key, MaxKeyLength, nameof(request.Key));
            if (request.Code != null)
                ThrowIfInvalidText(request.Code, MaxCodeLength, nameof(request.Code));
            if (request.DebitAccountId == request.CreditAccountId)
                throw new ArgumentException($"Transfer {request.Key} has one account on both sides.", nameof(requests));
            ThrowIfInvalidAmount(request.Amount, nameof(request.Amount));
        }

        if (requests.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new ArgumentException("A chain names one key twice.", nameof(requests));
    }

    private static void ThrowIfInvalidKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count is < 1 or > MaxChainLength)
            throw new ArgumentException($"1 to {MaxChainLength} keys are settled at once.", nameof(keys));

        foreach (var key in keys)
            ThrowIfInvalidText(key, MaxKeyLength, nameof(keys));

        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new ArgumentException("A key is named twice.", nameof(keys));
    }

    // The database would round a ninth decimal away without a word.
    private static void ThrowIfInvalidAmount(decimal amount, string paramName)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, AmountScale) != amount)
            throw new ArgumentOutOfRangeException(paramName, amount,
                $"An amount is above 0, at most {MaxAmount}, with at most {AmountScale} decimals.");
    }

    private static void ThrowIfInvalidLimit(decimal minBalance, string paramName)
    {
        if (Math.Abs(minBalance) > MaxAmount || decimal.Round(minBalance, AmountScale) != minBalance)
            throw new ArgumentOutOfRangeException(paramName, minBalance,
                $"A MinBalance is at most {MaxAmount} either way, with at most {AmountScale} decimals.");
    }

    // One spelling of a thing: no space at either end, which would make two of it, and SQL Server would not tell
    // apart.
    private static void ThrowIfInvalidText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Trim() != value ||
            value.Any(char.IsControl))
            throw new ArgumentException(
                $"{paramName} has 1 to {maxLength} characters, with no space at either end.", paramName);
    }

    // Upper case only, so USD and usd are never two currencies.
    private static void ThrowIfInvalidCurrency(string? currency)
    {
        if (string.IsNullOrEmpty(currency) || currency.Length > MaxCurrencyLength ||
            !currency.All(x => x is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new ArgumentException(
                $"A currency is 1 to {MaxCurrencyLength} upper-case letters and digits, such as USD.", nameof(currency));
    }
}
