﻿namespace EWallet.Dtos;

public sealed record CurrencyBalance
{
    public required int CurrencyId { get; init; }
    public required decimal Balance { get; init; }
    public required decimal MinBalance { get; init; }
}