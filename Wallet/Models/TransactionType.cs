using System.Text.Json.Serialization;

namespace EWallet.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransactionType : byte
{
    Authorize = 1,
    Sale = 2
}

public class TransactionTypeLookup(TransactionType transactionTypeId, string walletTransferTypeName)
{
    public TransactionType TransactionTypeId { get; set; } = transactionTypeId;
    public string WalletTransferTypeName { get; set; } = walletTransferTypeName;

    public virtual ICollection<OrderModel>? OrderModels { get; set; }
}