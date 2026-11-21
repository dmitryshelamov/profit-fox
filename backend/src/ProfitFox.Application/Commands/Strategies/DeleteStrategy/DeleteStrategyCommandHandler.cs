using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Signals;

namespace ProfitFox.Application.Commands.Strategies.DeleteStrategy;

internal sealed class DeleteStrategyCommandHandler(IStrategyRepository strategies) : IRequestHandler<DeleteStrategyCommand>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy.</exception>
    public async Task Handle(DeleteStrategyCommand request, CancellationToken cancellationToken)
    {
        if (!SignalNames.IsValid(request.Name) || !await strategies.ExistsAsync(request.Name, cancellationToken))
        {
            throw StrategyChecks.NotFound(request.Name);
        }

        await strategies.DeleteAsync(request.Name, cancellationToken);
    }
}
