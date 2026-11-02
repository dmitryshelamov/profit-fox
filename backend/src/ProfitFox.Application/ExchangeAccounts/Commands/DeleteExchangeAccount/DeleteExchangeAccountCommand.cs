using MediatR;

namespace ProfitFox.Application.ExchangeAccounts.Commands.DeleteExchangeAccount;

public sealed record DeleteExchangeAccountCommand(Guid Id) : IRequest;