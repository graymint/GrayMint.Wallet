﻿namespace EWallet.Dtos;

public sealed record ParticipantTransferItem
{
    public required int SenderWalletId { get; init; }
    public required int ReceiverWalletId { get; init; }
    public required decimal Amount { get; init; }
}