using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>A T-Invest token whose accounts are asked for; it is stored nowhere and never logged.</summary>
public sealed class BrokerTokenBody
{
    [Required]
    public string Token { get; init; } = string.Empty;
}