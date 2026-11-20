using System.Data.Common;
using System.Globalization;
using GrayMint.Common.Utils;
using GrayMint.Wallet.Wallets;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Server;
using GrayMint.Wallet.Transfers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GrayMint.Wallet.Test.Helper;

// A database of one test, on one of the two servers compose.yaml starts, with the wallet registered on it as a host
// registers it, on a connection of its own. It has a name nobody else uses and is dropped with the test.
public sealed class TestWallet : IAsyncDisposable
{
    // by address: "localhost" is tried over IPv6 first, where the containers don't listen
    private const string PostgreSqlServer = "Host=127.0.0.1;Port=5436;Username=postgres;Password=postgres";
    private const string SqlServerServer =
        "Server=127.0.0.1,14336;User ID=sa;Password=GrayMint-Wallet-2026;TrustServerCertificate=true";

    public const string TenantId = "1";
    public const string Currency = "USD";

    private readonly ServiceProvider _serviceProvider;

    public WalletDatabaseProvider Provider { get; }
    public string DatabaseName { get; } = $"test_{Guid.CreateVersion7():N}";

    public string ConnectionString => Provider == WalletDatabaseProvider.PostgreSql
        ? $"{PostgreSqlServer};Database={DatabaseName}"
        : $"{SqlServerServer};Database={DatabaseName}";

    private TestWallet(WalletDatabaseProvider provider)
    {
        Provider = provider;
        var services = new ServiceCollection();
        services.AddGrayMintWallet();
        services.AddGrayMintWalletDb(options => UseDatabase(options));
        _serviceProvider = services.BuildServiceProvider();
    }

    public static async Task<TestWallet> Create(WalletDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var testWallet = new TestWallet(provider);
        await testWallet.Setup(cancellationToken);
        return testWallet;
    }

    // only a name: a setup of the test's choice makes the database
    public static TestWallet CreateEmpty(WalletDatabaseProvider provider)
    {
        return new TestWallet(provider);
    }

    // the wallet's setup, as a host runs it
    public async Task Setup(CancellationToken cancellationToken)
    {
        await _serviceProvider.UseGrayMintWallet(cancellationToken);
    }

    // the wallet's services for one scope, as a request of a host gets them
    public TestWalletScope CreateScope()
    {
        return new TestWalletScope(_serviceProvider.CreateAsyncScope());
    }

    // the wallet's own context, for a test that looks behind its services
    internal WalletDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>();
        UseDatabase(options);
        return new WalletDbContext(options.Options);
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

    // a wallet with no limit, which money comes from, as a host's cash wallet
    public async Task<WalletInfo> CreateSource(CancellationToken cancellationToken, string tenantId = TenantId,
        string currency = Currency)
    {
        return await CreateWallet("source", null, cancellationToken, tenantId, currency);
    }

    public async Task<WalletInfo> CreateWallet(string ownerReferenceKey, decimal? minAvailableBalance,
        CancellationToken cancellationToken, string tenantId = TenantId, string currency = Currency)
    {
        await using var scope = CreateScope();
        return await scope.Wallets.Create(tenantId, new WalletCreateRequest {
            OwnerReferenceKey = ownerReferenceKey,
            WalletCode = "credit",
            Currency = currency,
            MinAvailableBalance = minAvailableBalance
        }, cancellationToken);
    }

    public async Task<WalletInfo> GetWallet(string walletId, CancellationToken cancellationToken,
        string tenantId = TenantId)
    {
        await using var scope = CreateScope();
        return await scope.Wallets.Get(tenantId, walletId, cancellationToken);
    }

    // a wallet's transactions, oldest first, as its statement reads
    public async Task<IReadOnlyList<WalletTransaction>> GetTransactions(WalletInfo wallet,
        CancellationToken cancellationToken)
    {
        await using var scope = CreateScope();
        var transactions = await scope.Wallets.GetTransactions(wallet.TenantId, wallet.WalletId, null,
            WalletLimits.MaxPageSize, cancellationToken);
        return transactions.Reverse().ToArray();
    }

    public async Task<IReadOnlyList<WalletTransfer>> Transfer(CancellationToken cancellationToken,
        params IReadOnlyList<WalletTransferCreateRequest> requests)
    {
        await using var scope = CreateScope();
        return await scope.Transfers.Create(TenantId, requests, cancellationToken);
    }

    public static WalletTransferCreateRequest Request(WalletInfo fromWallet, WalletInfo toWallet, decimal amount,
        bool isPending = false, string? externalReferenceKey = null)
    {
        return new WalletTransferCreateRequest {
            ExternalReferenceKey = externalReferenceKey ?? $"key-{Guid.CreateVersion7():N}",
            FromWalletId = fromWallet.WalletId,
            ToWalletId = toWallet.WalletId,
            Amount = amount,
            IsPending = isPending
        };
    }

    public static WalletUpdateRequest LimitUpdate(decimal? minAvailableBalance)
    {
        return new WalletUpdateRequest { MinAvailableBalance = new Patch<decimal?>(minAvailableBalance) };
    }

    // Locks a wallet's row as a call that writes it does, until the context's transaction ends.
    internal static async Task LockWallet(WalletDbContext dbContext, WalletInfo wallet,
        CancellationToken cancellationToken)
    {
        await dbContext.Wallets
            .FromSqlRaw(WalletDialect.For(dbContext.Database).LockWalletSql,
                int.Parse(wallet.TenantId, CultureInfo.InvariantCulture),
                int.Parse(wallet.WalletId, CultureInfo.InvariantCulture))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
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

    // SQL Server only. The setup's database reads committed rows by their versions, as EF makes one; from here it
    // reads them by locks, as a database SQL Server makes on its own does. No other connection may be open meanwhile.
    public async Task ReadCommittedRowsByLocks(CancellationToken cancellationToken)
    {
        SqlConnection.ClearPool(new SqlConnection(ConnectionString));
        await Execute("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF", cancellationToken);
    }

    // not cancellable: a cancelled test must still drop its database
    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
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
