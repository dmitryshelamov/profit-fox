using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies.Queries.ValidateStrategy;

/// <summary>Runs the library operation of the save on the loaded library and never saves it.</summary>
public sealed class ValidateStrategyQueryHandler(IStrategyLibraryRepository repository, TimeProvider time) : IRequestHandler<ValidateStrategyQuery, ValidationReport>
{
    /// <exception cref="NotFoundException">There is no strategy with the id.</exception>
    public async Task<ValidationReport> Handle(ValidateStrategyQuery request, CancellationToken cancellationToken)
    {
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (request.Id is { } id && library.Strategies.All(strategy => strategy.Id != id))
        {
            throw new NotFoundException($"There is no strategy {id}.");
        }

        return ValidationReport.Of(() =>
        {
            StrategyDefinition definition = StrategyRequests.Read(request.Name, request.Definition, StrategyJson.ParseStrategy);

            if (request.Id is { } existing)
            {
                StrategyRequests.WithBots(() => StrategyRequests.Check(() => library.UpdateStrategy(existing, request.Name, definition, time.GetUtcNow())));
            }
            else
            {
                StrategyRequests.Check(() => library.AddStrategy(Guid.NewGuid(), request.Name, definition, time.GetUtcNow()));
            }
        });
    }
}