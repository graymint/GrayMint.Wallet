using System.Text.Json.Serialization;

namespace EWallet.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransactionType : byte
{
    Authorize = 1,
    Sale = 2
}

public class TransactionTypeLookup
{
    public TransactionType TransactionTypeId { get; set; }
    public string WalletTransferTypeName { get; set; }
    public TransactionTypeLookup(TransactionType transactionTypeId, string walletTransferTypeName)
    {
        TransactionTypeId = transactionTypeId;
        WalletTransferTypeName = walletTransferTypeName;
    }

    public virtual ICollection<OrderModel>? OrderModels { get; set; }
}