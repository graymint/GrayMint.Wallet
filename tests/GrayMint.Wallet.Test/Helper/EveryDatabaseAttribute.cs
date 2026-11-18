using System.Reflection;
using GrayMint.Wallet.Server;

namespace GrayMint.Wallet.Test.Helper;

// Runs a test once on each database the wallet runs on.
[AttributeUsage(AttributeTargets.Method)]
public sealed class EveryDatabaseAttribute : Attribute, ITestDataSource
{
    public IEnumerable<object?[]> GetData(MethodInfo methodInfo)
    {
        return Enum.GetValues<WalletDatabaseProvider>().Select(x => new object?[] { x });
    }

    public string GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        return $"{methodInfo.Name} ({data?[0]})";
    }
}
