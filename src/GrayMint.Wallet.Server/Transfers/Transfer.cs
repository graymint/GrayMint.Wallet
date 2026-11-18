using System.Text.Json.Serialization;
using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Server.Transfers;

// A transfer is never changed or deleted once written: a mistake is put right by a transfer the other way, and a
// pending one is settled by a second transfer, its post or its void.
public class Transfer
{
    public required string TransferId { get; init; }
    public required string LedgerId { get; init; }
    public required string Currency { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<WalletTransferType>))]
    public required WalletTransferType Type { get; init; }

    // the caller's idempotency key, unique in its ledger; a post or a void has none, as it is one per pending
    public required string? Key { get; init; }

    public required string DebitAccountId { get; init; }
    public required string CreditAccountId { get; init; }
    public required decimal Amount { get; init; }

    // what the transfer is for, in the caller's own terms
    public required string? Code { get; init; }

    // the pending transfer that a post or a void settles
    public required string? PendingTransferId { get; init; }

    public required DateTime CreatedTime { get; init; }
}
