using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GrayMint.Wallet.Test.Helper;

// Counts the connections a context opens, as a host's interceptor that tells the database who a connection works for
// would see them.
public sealed class TestConnectionCounter : DbConnectionInterceptor
{
    private int _openedCount;

    public int OpenedCount => _openedCount;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Interlocked.Increment(ref _openedCount);
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _openedCount);
        return Task.CompletedTask;
    }
}
