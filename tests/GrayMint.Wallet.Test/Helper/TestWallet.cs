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

    // the test's token, which every call of this database's takes
    public CancellationToken CancellationToken { get; }
    public string DatabaseName { get; } = $"test_{Guid.CreateVersion7():N}";

    public string ConnectionString => Provider == WalletDatabaseProvider.PostgreSql
        ? $"{PostgreSqlServer};Database={DatabaseName}"
        : $"{SqlServerServer};Database={DatabaseName}";

    private TestWallet(WalletDatabaseProvider provider, CancellationToken cancellationToken)
    {
        Provider = provider;
        CancellationToken = cancellationToken;
        var services = new ServiceCollection();
        services.AddGrayMintWallet();
        services.AddGrayMintWalletDb(options => UseDatabase(options));
        _serviceProvider = services.BuildServiceProvider();
    }

    public static async Task<TestWallet> Create(WalletDatabaseProvider provider, CancellationToken cancellationToken = default)
    {
        var testWallet = new TestWallet(provider, cancellationToken);
        try {
            await testWallet.Setup();
            return testWallet;
        }
        catch {
            // no test gets it to drop, so its database goes here
            await testWallet.DisposeAsync();
            throw;
        }
    }

    // only a name: a setup of the test's choice makes the database
    public static TestWallet CreateEmpty(WalletDatabaseProvider provider, CancellationToken cancellationToken = default)
    {
        return new TestWallet(provider, cancellationToken);
    }

    // the wallet's setup, as a host runs it
    public async Task Setup()
    {
        await _serviceProvider.UseGrayMintWallet(CancellationToken);
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
    public async Task<WalletInfo> CreateSource(string tenantId = TenantId, string currency = Currency)
    {
        return await CreateWallet("source", null, tenantId, currency);
    }

    public async Task<WalletInfo> CreateWallet(string ownerReferenceKey, decimal? minAvailableBalance,
        string tenantId = TenantId, string currency = Currency)
    {
        await using var scope = CreateScope();
        return await scope.Wallets.Create(tenantId, new WalletCreateRequest {
            OwnerReferenceKey = ownerReferenceKey,
            WalletCode = "credit",
            Currency = currency,
            MinAvailableBalance = minAvailableBalance
        }, CancellationToken);
    }

    public async Task<WalletInfo> GetWallet(string walletId, string tenantId = TenantId)
    {
        await using var scope = CreateScope();
        return await scope.Wallets.Get(tenantId, walletId, CancellationToken);
    }

    // a wallet's transactions, oldest first, as its statement reads
    public async Task<IReadOnlyList<WalletTransaction>> GetTransactions(WalletInfo wallet)
    {
        await using var scope = CreateScope();
        var transactions = await scope.Wallets.GetTransactions(wallet.TenantId, wallet.WalletId, null,
            WalletLimits.MaxPageSize, CancellationToken);

        return transactions.Reverse().ToArray();
    }

    public async Task<IReadOnlyList<WalletTransfer>> Transfer(params IReadOnlyList<WalletTransferCreateRequest> requests)
    {
        await using var scope = CreateScope();
        return await scope.Transfers.Create(TenantId, requests, CancellationToken);
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
        CancellationToken cancellationToken = default)
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
    public async Task<int> Execute(string sql)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync(CancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(CancellationToken);
    }

    public Task<long> Count(string sql)
    {
        return Count(sql, CancellationToken);
    }

    private async Task<long> Count(string sql, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    // Waits until as many calls as given wait for a lock another holds, so a test knows they got that far.
    public async Task WaitForBlockedCalls(int count)
    {
        var sql = Provider == WalletDatabaseProvider.PostgreSql
            ? "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'"
            : "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id <> 0";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (await Count(sql, timeout.Token) < count)
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    }

    // SQL Server only. The setup's database reads committed rows by their versions, as EF makes one; from here it
    // reads them by locks, as a database SQL Server makes on its own does. No other connection may be open meanwhile.
    public async Task ReadCommittedRowsByLocks()
    {
        SqlConnection.ClearPool(new SqlConnection(ConnectionString));
        await Execute("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF");
    }

    // The test's database, keeping text in a single-byte code page, as an old database might: LATIN1 on PostgreSQL,
    // SQL_Latin1_General_CP1_CI_AS on SQL Server.
    public Task CreateDatabaseWithoutUtf8()
    {
        return ExecuteOnServer(Provider == WalletDatabaseProvider.PostgreSql
            ? $"CREATE DATABASE \"{DatabaseName}\" ENCODING 'LATIN1' LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0"
            : $"CREATE DATABASE [{DatabaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS", CancellationToken);
    }

    // A deadline of its own, not the test's token: a cancelled test must still drop its database. A minute ends only
    // a drop the server never answers.
    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        if (Provider == WalletDatabaseProvider.SqlServer)
            SqlConnection.ClearPool(new SqlConnection(ConnectionString));

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await ExecuteOnServer(Provider == WalletDatabaseProvider.PostgreSql
            ? $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)"
            : $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
              $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
              $"DROP DATABASE [{DatabaseName}]; END", deadline.Token);
    }

    // a command on the server's own database, for one that makes or drops the test's
    private async Task ExecuteOnServer(string sql, CancellationToken cancellationToken)
    {
        await using DbConnection connection = Provider == WalletDatabaseProvider.PostgreSql
            ? new NpgsqlConnection($"{PostgreSqlServer};Database=postgres;Pooling=false")
            : new SqlConnection($"{SqlServerServer};Database=master;Pooling=false");

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
