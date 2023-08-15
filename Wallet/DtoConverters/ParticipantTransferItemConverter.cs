using EWallet.Dtos;

namespace EWallet.DtoConverters;

public static class ParticipantTransferItemConverter
{
    public static List<int> GetWalletIds(this List<ParticipantTransferItem> items)
    {
        // get list senders
        var list = items.Select(x => x.SenderWalletId)
            .Distinct()
            .ToList();

        // add receivers to list
        list.AddRange(items.Select(x => x.ReceiverWalletId)
            .Distinct()
            .ToList());

        return list
            .Distinct()
            .ToList();
    }
}