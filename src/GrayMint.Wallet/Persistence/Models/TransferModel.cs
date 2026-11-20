using GrayMint.Wallet.Transfers;

namespace GrayMint.Wallet.Persistence.Models;

// A transfer's row, never changed or deleted once written: a mistake is put right by a transfer the other way, and a
// pending one is settled by a second row, its post or its void.
internal sealed class TransferModel
{
    public long TransferId { get; init; }
    public required int TenantId { get; init; }
    public required string Currency { get; init; }
    public required WalletTransferType Type { get; init; }

    // the caller's external reference key, unique in its tenant; a post or a void has none, as it is one per pending
    public string? ExternalReferenceKey { get; init; }

    public required int FromWalletId { get; init; }
    public required int ToWalletId { get; init; }
    public required decimal Amount { get; init; }
    public string? PurposeCode { get; init; }

    // the pending transfer that a post or a void settles
    public long? PendingTransferId { get; init; }

    public DateTime CreatedTime { get; init; }
}
