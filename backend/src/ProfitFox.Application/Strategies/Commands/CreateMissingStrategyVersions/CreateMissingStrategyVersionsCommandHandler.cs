using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Strategies.Commands.CreateMissingStrategyVersions;

public sealed class CreateMissingStrategyVersionsCommandHandler(IStrategyLibraryRepository repository, TimeProvider time)
    : IRequestHandler<CreateMissingStrategyVersionsCommand>
{
    /// <summary>Saves nothing, not even a new revision, when every strategy has a version.</summary>
    /// <exception cref="StateConflictException">The library changed meanwhile; nothing serves requests yet, so this does not happen.</exception>
    public async Task Handle(CreateMissingStrategyVersionsCommand request, CancellationToken cancellationToken)
    {
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        library.CreateMissingVersions(time.GetUtcNow());

        if (library.Change.NewVersions.Count > 0)
        {
            StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
        }
    }
}