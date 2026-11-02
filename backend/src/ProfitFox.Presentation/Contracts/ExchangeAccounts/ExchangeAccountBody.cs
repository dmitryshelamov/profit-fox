using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>A new exchange account; the secret is stored encrypted and never returned.</summary>
public sealed class ExchangeAccountBody
{
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary><c>mainnet</c> or <c>testnet</c>.</summary>
    [Required]
    public string Environment { get; init; } = string.Empty;

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string ApiSecret { get; init; } = string.Empty;
}