using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.ValidateNode;

/// <summary>Runs the library operation of the save on the loaded library and never saves it.</summary>
public sealed class ValidateNodeQueryHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<ValidateNodeQuery, ValidationReport>
{
    /// <exception cref="StateConflictException">The id names a built-in node, which cannot be changed.</exception>
    /// <exception cref="NotFoundException">There is no node with the id.</exception>
    public async Task<ValidationReport> Handle(ValidateNodeQuery request, CancellationToken cancellationToken)
    {
        Guid? id = request.Id is null ? null : NodeIds.Custom(request.Id);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (id is { } existing && library.Composites.All(node => node.Id != existing))
        {
            throw NodeIds.NotFound(request.Id!);
        }

        return ValidationReport.Of(() =>
        {
            CompositeDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseComposite);

            if (id is { } changed)
            {
                NodeIds.InUse(() => StrategyRequests.Check(() => library.UpdateComposite(changed, request.Name, definition, time.GetUtcNow())));
            }
            else
            {
                StrategyRequests.Check(() => library.AddComposite(Guid.NewGuid(), request.Name, definition, time.GetUtcNow()));
            }
        });
    }
}