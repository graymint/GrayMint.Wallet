using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GrayMint.Wallet.Persistence;

// One call's hold on the database.
// On a host's connection, the host's context opens it, so the host's connection interceptors run as they do for its
// own commands, and the call works in the transaction the host has open.
// A call that writes inside a transaction it did not begin has a savepoint: when it fails, what it wrote is taken
// back and the transaction is left usable, which PostgreSQL would not do after an error. A call that writes with no
// transaction open runs in one of its own.
internal sealed class WalletSession : IAsyncDisposable
{
    private const string SavepointName = "GrayMintWallet";

    private readonly DatabaseFacade? _hostDatabase;
    private readonly IDbContextTransaction? _ownTransaction;
    private readonly IDbContextTransaction? _savepointTransaction;
    private bool _isCommitted;

    private WalletSession(DatabaseFacade? hostDatabase, IDbContextTransaction? ownTransaction,
        IDbContextTransaction? savepointTransaction)
    {
        _hostDatabase = hostDatabase;
        _ownTransaction = ownTransaction;
        _savepointTransaction = savepointTransaction;
    }

    public static async Task<WalletSession> Begin(WalletDbContext walletDbContext, HostDatabase? hostDatabase,
        bool isWrite, CancellationToken cancellationToken)
    {
        var database = walletDbContext.Database;
        if (hostDatabase != null)
            await hostDatabase.Database.OpenConnectionAsync(cancellationToken);

        try {
            if (hostDatabase != null)
                await JoinHostTransaction(database, hostDatabase.Database, cancellationToken);

            if (!isWrite)
                return new WalletSession(hostDatabase?.Database, null, null);

            if (database.CurrentTransaction == null)
                return new WalletSession(hostDatabase?.Database,
                    await database.BeginTransactionAsync(cancellationToken), null);

            await database.CurrentTransaction.CreateSavepointAsync(SavepointName, cancellationToken);
            return new WalletSession(hostDatabase?.Database, null, database.CurrentTransaction);
        }
        catch {
            if (hostDatabase != null)
                await hostDatabase.Database.CloseConnectionAsync();
            throw;
        }
    }

    // The wallet's context holds on to a host's transaction it joined until it is told otherwise, after the host
    // has ended it too.
    private static async Task JoinHostTransaction(DatabaseFacade database, DatabaseFacade hostDatabase,
        CancellationToken cancellationToken)
    {
        var hostTransaction = hostDatabase.CurrentTransaction?.GetDbTransaction();
        if (database.CurrentTransaction?.GetDbTransaction() == hostTransaction)
            return;

        if (database.CurrentTransaction != null)
            await database.UseTransactionAsync(null, cancellationToken);
        if (hostTransaction != null)
            await database.UseTransactionAsync(hostTransaction, cancellationToken);
    }

    public async Task Commit(CancellationToken cancellationToken)
    {
        if (_ownTransaction != null)
            await _ownTransaction.CommitAsync(cancellationToken);
        if (_savepointTransaction != null)
            await _savepointTransaction.ReleaseSavepointAsync(SavepointName, cancellationToken);
        _isCommitted = true;
    }

    // On SQL Server a deadlock, or an error with XACT_ABORT on, ends the whole transaction, the host's with it:
    // nothing is left to take back or to look in, and the error says what happened.
    public static bool IsTransactionEnded(DatabaseFacade database)
    {
        return database.CurrentTransaction != null &&
               database.CurrentTransaction.GetDbTransaction().Connection == null;
    }

    // A deadline of its own, not the call's token, which may be canceled already: what a failed call wrote must be
    // taken back. A minute ends only a rollback the database never answers.
    public async ValueTask DisposeAsync()
    {
        try {
            if (_ownTransaction != null)
                await _ownTransaction.DisposeAsync();
            else if (_savepointTransaction != null && !_isCommitted &&
                     _savepointTransaction.GetDbTransaction().Connection != null) {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                await _savepointTransaction.RollbackToSavepointAsync(SavepointName, deadline.Token);
            }
        }
        finally {
            if (_hostDatabase != null)
                await _hostDatabase.CloseConnectionAsync();
        }
    }
}
