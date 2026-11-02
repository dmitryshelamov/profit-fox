using System.ComponentModel.DataAnnotations;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

public sealed class RenameExchangeAccountBody
{
    [Required]
    public string Name { get; init; } = string.Empty;
}