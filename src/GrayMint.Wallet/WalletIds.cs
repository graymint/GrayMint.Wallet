using System.Globalization;
using System.Numerics;
using GrayMint.Common.Exceptions;

namespace GrayMint.Wallet;

// Every id goes out as text, so a key's type inside can change without the API changing. An id that is no number
// names nothing, so it is refused as one that is not there.
internal static class WalletIds
{
    public static string Format(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    // a tenant is the host's number, which may be below 0
    public static int ParseTenantId(string tenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        return int.TryParse(tenantId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NotExistsException($"Could not find the tenant. Id: {tenantId}");
    }

    public static int ParseWalletId(string walletId)
    {
        return ParseId<int>(walletId, "wallet");
    }

    public static long ParseTransferId(string transferId)
    {
        return ParseId<long>(transferId, "transfer");
    }

    // a number beyond the key's type names nothing either
    private static T ParseId<T>(string id, string entityName) where T : IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(id);
        return T.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NotExistsException($"Could not find the {entityName}. Id: {id}");
    }
}
