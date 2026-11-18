using System.Globalization;
using GrayMint.Common.Exceptions;

namespace GrayMint.Wallet.Server;

// Every id goes out as text, so a key's type inside can change without the API changing. An id that is no number
// names nothing, so it is answered as one that is not there.
public static class ServerIds
{
    public static string Format(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    // a ledger is the caller's number, which may be below 0
    public static int ParseLedgerId(string id)
    {
        return int.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NotExistsException($"Could not find the ledger. Id: {id}");
    }

    public static long ParseLongId(string id, string entityName)
    {
        return long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NotExistsException($"Could not find the {entityName}. Id: {id}");
    }

    public static long? ParseLongIdOrNull(string? id, string entityName)
    {
        return id == null ? null : ParseLongId(id, entityName);
    }
}
