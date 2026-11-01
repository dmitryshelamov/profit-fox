using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Commands.UpdateStrategy;

public sealed class UpdateStrategyCommandHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<UpdateStrategyCommand>
{
    /// <exception cref="NotFoundException">There is no such strategy.</exception>
    /// <exception cref="ValidationFailedException">The name is empty, or the definition is malformed or invalid.</exception>
    /// <exception cref="StateConflictException">The library changed while the strategy was being saved.</exception>
    public async Task Handle(UpdateStrategyCommand request, CancellationToken cancellationToken)
    {
        StrategyDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseStrategy);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        _ = StrategyRequests.Check(() => library.UpdateStrategy(request.Id, request.Name, definition, time.GetUtcNow()))
            ?? throw new NotFoundException($"There is no strategy {request.Id}.");
        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
    }
}