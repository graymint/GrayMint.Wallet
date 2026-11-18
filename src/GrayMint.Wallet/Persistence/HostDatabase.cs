using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GrayMint.Wallet.Persistence;

// The host's context, whose connection the wallet's context works on (WalletExtension.AddGrayMintWalletDb).
internal sealed class HostDatabase(DatabaseFacade database)
{
    public DatabaseFacade Database { get; } = database;
}
