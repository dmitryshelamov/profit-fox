using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Commands.CreateStrategy;

public sealed class CreateStrategyCommandHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<CreateStrategyCommand, Guid>
{
    /// <exception cref="ValidationFailedException">The name is empty, or the definition is malformed or invalid.</exception>
    /// <exception cref="StateConflictException">The library changed while the strategy was being saved.</exception>
    public async Task<Guid> Handle(CreateStrategyCommand request, CancellationToken cancellationToken)
    {
        StrategyDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseStrategy);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        Strategy strategy = StrategyRequests.Check(() => library.AddStrategy(Guid.NewGuid(), request.Name, definition, time.GetUtcNow()));
        StrategyRequests.EnsureSaved(await repository.SaveStrategyAsync(strategy, library.Revision, cancellationToken));

        return strategy.Id;
    }
}