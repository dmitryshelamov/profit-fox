using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>
///     A new exchange account. <c>exchange</c> is <c>bybit</c> (the default, for a body without it) with <c>apiKey</c> and <c>apiSecret</c>,
///     or <c>tinvest</c> with <c>token</c> and <c>accountId</c>, the number of the brokerage account. The secret and the token are stored
///     encrypted and never returned.
/// </summary>
public sealed class ExchangeAccountBody
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public string Exchange { get; init; } = "bybit";

    public string? ApiKey { get; init; }

    public string? ApiSecret { get; init; }

    public string? Token { get; init; }

    public string? AccountId { get; init; }
}