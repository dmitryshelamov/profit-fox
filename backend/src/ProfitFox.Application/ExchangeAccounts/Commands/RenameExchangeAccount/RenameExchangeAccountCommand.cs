using MediatR;

namespace ProfitFox.Application.ExchangeAccounts.Commands.RenameExchangeAccount;

public sealed record RenameExchangeAccountCommand(Guid Id, string Name) : IRequest;