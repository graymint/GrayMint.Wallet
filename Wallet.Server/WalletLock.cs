using GrayMint.Common.Utils;

namespace EWallet.Server;

public static class WalletLock
{
    private static async Task<AsyncLock.ILockAsyncResult> LockObjectAsync(string lockName)
    {
        var result = await AsyncLock.LockAsync(lockName, TimeSpan.FromMinutes(10));
        if (!result.Succeeded)
            throw new TimeoutException($"Lock item has reached timeout, lockName: {lockName}");

        return result;
    }

    public static Task<AsyncLock.ILockAsyncResult> LockOrder(string orderId)
    {
        var lockName = $"order:{orderId}";
        return LockObjectAsync(lockName);
    }

    //public static Task<AsyncLock.ILockAsyncResult> LockWallets(int[] walletIds)
    //{
    //    //var lockName = $"product:{merchantId}-{productItemId}";
    //    //return LockObjectAsync(lockName);
    //}
}