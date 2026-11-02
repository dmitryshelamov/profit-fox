using MediatR;

namespace ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;

/// <summary>A new exchange account from the keys the user sent; returns its id.</summary>
public sealed record CreateExchangeAccountCommand(string Name, string Environment, string ApiKey, string ApiSecret) : IRequest<Guid>;