using MediatR;

using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;

/// <summary>
///     A new exchange account from what the user sent: on Bybit the API key and secret, on T-Invest the token and the number of the
///     brokerage account; returns its id.
/// </summary>
public sealed record CreateExchangeAccountCommand(string Name, Exchange Exchange, string? ApiKey, string? ApiSecret, string? Token, string? AccountId) : IRequest<Guid>;