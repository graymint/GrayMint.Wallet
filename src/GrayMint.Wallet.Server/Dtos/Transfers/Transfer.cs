using System.Text.Json.Serialization;

namespace GrayMint.Wallet.Server.Dtos.Transfers;

/// <summary>
/// An amount moved from one wallet to another of its tenant and currency. A transfer is never changed or deleted: a
/// mistake is put right by a transfer the other way, and a pending one is settled by a second transfer, its post or
/// its void.
/// </summary>
public class Transfer
{
    /// <summary>The transfer's id.</summary>
    public required string TransferId { get; init; }

    /// <summary>The tenant the transfer is in.</summary>
    public required string TenantId { get; init; }

    /// <summary>The currency of the amount and of both wallets.</summary>
    public required string Currency { get; init; }

    /// <summary>What the transfer does.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TransferType>))]
    public required TransferType Type { get; init; }

    /// <summary>
    /// The caller's external reference key, unique in the tenant. Null for a post or a void, which is known by the
    /// one pending transfer it settles.
    /// </summary>
    public required string? ExternalReferenceKey { get; init; }

    /// <summary>The wallet the amount comes from.</summary>
    public required string FromWalletId { get; init; }

    /// <summary>The wallet the amount goes to.</summary>
    public required string ToWalletId { get; init; }

    /// <summary>The amount, above 0.</summary>
    public required decimal Amount { get; init; }

    /// <summary>What the transfer is for, in the caller's own terms; a post or a void has its pending transfer's.</summary>
    public required string? PurposeCode { get; init; }

    /// <summary>The pending transfer that a post or a void settles; null for any other transfer.</summary>
    public required string? PendingTransferId { get; init; }

    /// <summary>When the transfer was made, in UTC.</summary>
    public required DateTime CreatedTime { get; init; }
}
