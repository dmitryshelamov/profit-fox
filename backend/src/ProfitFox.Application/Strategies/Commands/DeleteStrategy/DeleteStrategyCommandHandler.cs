using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Commands.DeleteStrategy;

public sealed class DeleteStrategyCommandHandler(IStrategyLibraryRepository repository) : IRequestHandler<DeleteStrategyCommand>
{
    /// <exception cref="NotFoundException">There is no such strategy.</exception>
    /// <exception cref="StateConflictException">The library changed while the strategy was being deleted.</exception>
    public async Task Handle(DeleteStrategyCommand request, CancellationToken cancellationToken)
    {
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (!library.RemoveStrategy(request.Id))
        {
            throw new NotFoundException($"There is no strategy {request.Id}.");
        }

        StrategyRequests.EnsureSaved(await repository.DeleteStrategyAsync(request.Id, library.Revision, cancellationToken));
    }
}