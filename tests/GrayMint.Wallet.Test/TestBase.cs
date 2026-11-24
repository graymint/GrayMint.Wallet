using GrayMint.Wallet.Server;
using GrayMint.Wallet.Test.Helper;

namespace GrayMint.Wallet.Test;

// Every test class derives from it. A test takes its cancellation token here, once: the wallet it makes through these
// methods keeps the token and passes it on, so a test method names it only where it calls the wallet's services directly.
public abstract class TestBase
{
    public required TestContext TestContext { get; set; }

    protected CancellationToken TestCt => TestContext.CancellationToken;

    protected Task<TestWallet> CreateTestWallet(WalletDatabaseProvider provider)
    {
        return TestWallet.Create(provider, TestCt);
    }

    // only a name: a setup of the test's choice makes the database
    protected TestWallet CreateEmptyTestWallet(WalletDatabaseProvider provider)
    {
        return TestWallet.CreateEmpty(provider, TestCt);
    }

    protected Task<TestHost> CreateTestHost(WalletDatabaseProvider provider)
    {
        return TestHost.Create(provider, TestCt);
    }

    protected Task<TestWalletServer> CreateTestWalletServer(WalletDatabaseProvider provider,
        IReadOnlyList<string>? apiKeys = null)
    {
        return TestWalletServer.Create(provider, apiKeys, TestCt);
    }
}
