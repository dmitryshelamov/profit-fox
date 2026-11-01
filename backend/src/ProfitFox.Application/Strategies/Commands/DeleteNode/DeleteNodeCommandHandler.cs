using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Commands.DeleteNode;

public sealed class DeleteNodeCommandHandler(IStrategyLibraryRepository repository) : IRequestHandler<DeleteNodeCommand>
{
    /// <exception cref="StateConflictException">The node is built in, a graph uses it, or the library changed.</exception>
    /// <exception cref="NotFoundException">There is no such node.</exception>
    public async Task Handle(DeleteNodeCommand request, CancellationToken cancellationToken)
    {
        Guid id = NodeIds.Custom(request.Id);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (!NodeIds.InUse(() => library.RemoveComposite(id)))
        {
            throw NodeIds.NotFound(request.Id);
        }

        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
    }
}