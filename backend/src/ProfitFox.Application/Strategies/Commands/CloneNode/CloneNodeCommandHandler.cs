using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Commands.CloneNode;

public sealed class CloneNodeCommandHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<CloneNodeCommand, Guid>
{
    /// <exception cref="ValidationFailedException">The given name is empty.</exception>
    /// <exception cref="NotFoundException">There is no such node.</exception>
    /// <exception cref="StateConflictException">The library changed while the clone was being saved.</exception>
    public async Task<Guid> Handle(CloneNodeCommand request, CancellationToken cancellationToken)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["name"] = "Name must not be empty." });
        }

        StrategyJson.TryParseNodeId(request.Id, out CompositeNodeId? source);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (source is null || library.Catalog().Find(source) is null)
        {
            throw NodeIds.NotFound(request.Id);
        }

        string name = request.Name ?? $"{NodeTitles.Of(source, library)} (копия)";
        CompositeNode clone = library.CloneComposite(source, Guid.NewGuid(), name, time.GetUtcNow())!;
        StrategyRequests.EnsureSaved(await repository.SaveCompositeAsync(clone, library.Revision, cancellationToken));

        return clone.Id;
    }
}