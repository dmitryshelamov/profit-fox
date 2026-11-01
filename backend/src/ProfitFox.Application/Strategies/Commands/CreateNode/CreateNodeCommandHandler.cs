using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Commands.CreateNode;

public sealed class CreateNodeCommandHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<CreateNodeCommand, Guid>
{
    /// <exception cref="ValidationFailedException">The name is empty, or the definition is malformed or invalid.</exception>
    /// <exception cref="StateConflictException">The library changed while the node was being saved.</exception>
    public async Task<Guid> Handle(CreateNodeCommand request, CancellationToken cancellationToken)
    {
        CompositeDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseComposite);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        CompositeNode node = StrategyRequests.Check(() => library.AddComposite(Guid.NewGuid(), request.Name, definition, time.GetUtcNow()));
        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));

        return node.Id;
    }
}