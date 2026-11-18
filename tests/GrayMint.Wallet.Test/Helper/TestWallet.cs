using System.Data.Common;
using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Transfers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrayMint.Wallet.Test.Helper;

// A database of one test, on one of the two servers compose.yaml starts, made as the wallet's setup makes it. It has
// a name nobody else uses and is dropped with the test.
public sealed class TestWallet : IAsyncDisposable
{
    // by address: "localhost" is tried over IPv6 first, where the containers don't listen
    private const string PostgreSqlServer = "Host=127.0.0.1;Port=5436;Username=postgres;Password=postgres";
    private const string SqlServerServer =
        "Server=127.0.0.1,14336;User ID=sa;Password=GrayMint-Wallet-2026;TrustServerCertificate=true";

    public const int LedgerId = 1;
    public const string Currency = "USD";

    public WalletDatabaseProvider Provider { get; }
    public string DatabaseName { get; } = $"test_{Guid.CreateVersion7():N}";

    public string ConnectionString => Provider == WalletDatabaseProvider.PostgreSql
        ? $"{PostgreSqlServer};Database={DatabaseName}"
        : $"{SqlServerServer};Database={DatabaseName}";

    private TestWallet(WalletDatabaseProvider provider)
    {
        Provider = provider;
    }

    public static async Task<TestWallet> Create(WalletDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var testWallet = new TestWallet(provider);
        await using var dbContext = testWallet.CreateDbContext();
        await WalletSetup.Run(dbContext, hostDatabase: null, cancellationToken);
        return testWallet;
    }

    // only a name: a setup of the test's choice makes the database
    public static TestWallet CreateEmpty(WalletDatabaseProvider provider)
    {
        return new TestWallet(provider);
    }

    // the model's tables without the guards, as a database whose guards were dropped
    public static async Task<TestWallet> CreateWithoutGuards(WalletDatabaseProvider provider,
        CancellationToken cancellationToken)
    {
        var testWallet = new TestWallet(provider);
        await using var dbContext = testWallet.CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        return testWallet;
    }

    public WalletDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>();
        UseDatabase(options);
        return new WalletDbContext(options.Options);
    }

    public static WalletService CreateService(WalletDbContext dbContext)
    {
        return new WalletService(dbContext, hostDatabase: null);
    }

    // a context on this database, as a host gives it
    public DbContextOptionsBuilder UseDatabase(DbContextOptionsBuilder options)
    {
        return Provider == WalletDatabaseProvider.PostgreSql
            ? options.UseNpgsql(ConnectionString)
            : options.UseSqlServer(ConnectionString);
    }

    // a context whose strategy retries on failure
    public DbContextOptionsBuilder UseDatabaseRetrying(DbContextOptionsBuilder options)
    {
        return Provider == WalletDatabaseProvider.PostgreSql
            ? options.UseNpgsql(ConnectionString, x => x.EnableRetryOnFailure())
            : options.UseSqlServer(ConnectionString, x => x.EnableRetryOnFailure());
    }

    // a context on the host's own connection
    public DbContextOptionsBuilder UseDatabase(DbContextOptionsBuilder options, DbConnection connection)
    {
        return Provider == WalletDatabaseProvider.PostgreSql
            ? options.UseNpgsql(connection)
            : options.UseSqlServer(connection);
    }

    // an account with no limit, which money comes from, as a host's cash account
    public async Task<WalletAccount> CreateSource(CancellationToken cancellationToken, int ledgerId = LedgerId,
        string currency = Currency)
    {
        return await CreateAccount("source", null, cancellationToken, ledgerId, currency);
    }

    public async Task<WalletAccount> CreateAccount(string ownerId, decimal? minBalance,
        CancellationToken cancellationToken, int ledgerId = LedgerId, string currency = Currency)
    {
        await using var dbContext = CreateDbContext();
        return await CreateService(dbContext).CreateAccount(ledgerId, new WalletAccountCreateRequest {
            OwnerId = ownerId,
            Code = "credit",
            Currency = currency,
            MinBalance = minBalance
        }, cancellationToken);
    }

    public async Task<WalletAccount> GetAccount(long accountId, CancellationToken cancellationToken,
        int ledgerId = LedgerId)
    {
        await using var dbContext = CreateDbContext();
        return await CreateService(dbContext).GetAccount(ledgerId, accountId, cancellationToken);
    }

    public async Task<IReadOnlyList<WalletTransfer>> Transfer(CancellationToken cancellationToken,
        params IReadOnlyList<WalletTransferRequest> requests)
    {
        await using var dbContext = CreateDbContext();
        return await CreateService(dbContext).CreateTransfers(LedgerId, requests, cancellationToken);
    }

    public static WalletTransferRequest Request(WalletAccount debit, WalletAccount credit, decimal amount,
        bool isPending = false, string? key = null)
    {
        return new WalletTransferRequest {
            Key = key ?? $"key-{Guid.CreateVersion7():N}",
            DebitAccountId = debit.AccountId,
            CreditAccountId = credit.AccountId,
            Amount = amount,
            IsPending = isPending
        };
    }

    // What a test does behind the wallet's back, as the owner of the tables. Names are quoted "so", which both
    // databases read.
    public async Task<int> Execute(string sql, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> Count(string sql, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    // Waits until as many calls as given wait for a lock another holds, so a test knows they got that far.
    public async Task WaitForBlockedCalls(int count, CancellationToken cancellationToken)
    {
        var sql = Provider == WalletDatabaseProvider.PostgreSql
            ? "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'"
            : "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id <> 0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (await Count(sql, timeout.Token) < count)
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    }

    // not cancellable: a cancelled test must still drop its database
    public async ValueTask DisposeAsync()
    {
        if (Provider == WalletDatabaseProvider.PostgreSql) {
            await using var connection = new NpgsqlConnection($"{PostgreSqlServer};Database=postgres;Pooling=false");
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            return;
        }

        SqlConnection.ClearPool(new SqlConnection(ConnectionString));
        await using (var connection = new SqlConnection($"{SqlServerServer};Database=master;Pooling=false")) {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new SqlCommand(
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{DatabaseName}]; END", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
