using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Commands.UpdateNode;

public sealed class UpdateNodeCommandHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<UpdateNodeCommand>
{
    /// <exception cref="StateConflictException">The node is built in, a graph that uses it would break, or the library changed.</exception>
    /// <exception cref="NotFoundException">There is no such node.</exception>
    /// <exception cref="ValidationFailedException">The name is empty, or the definition is malformed or invalid.</exception>
    public async Task Handle(UpdateNodeCommand request, CancellationToken cancellationToken)
    {
        Guid id = NodeIds.Custom(request.Id);
        CompositeDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseComposite);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        _ = NodeIds.InUse(() => StrategyRequests.Check(() => library.UpdateComposite(id, request.Name, definition, time.GetUtcNow())))
            ?? throw NodeIds.NotFound(request.Id);
        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
    }
}