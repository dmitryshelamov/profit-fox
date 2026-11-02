using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Commands.RenameExchangeAccount;

public sealed class RenameExchangeAccountCommandHandler(IExchangeAccountRepository repository) : IRequestHandler<RenameExchangeAccountCommand>
{
    /// <summary>Only the name changes; the keys, the environment and what the exchange told about the key stay.</summary>
    /// <exception cref="ValidationFailedException">The name is empty.</exception>
    /// <exception cref="NotFoundException">There is no such account.</exception>
    public async Task Handle(RenameExchangeAccountCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["name"] = "Name must not be empty." });
        }

        if (!await repository.RenameAsync(request.Id, request.Name, cancellationToken))
        {
            throw new NotFoundException($"There is no exchange account {request.Id}.");
        }
    }
}