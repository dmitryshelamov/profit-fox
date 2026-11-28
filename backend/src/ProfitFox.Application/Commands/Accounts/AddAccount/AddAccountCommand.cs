using MediatR;

using ProfitFox.Domain.Entities.Accounts;

namespace ProfitFox.Application.Commands.Accounts.AddAccount;

/// <summary>
///     Adds an account of Bybit by its name, API key and secret (spec exchange-accounts «Добавление аккаунта», «Проверка ключа
///     у Bybit»).
/// </summary>
public sealed record AddAccountCommand(string? Name, string? ApiKey, string? Secret) : IRequest<ExchangeAccount>;
