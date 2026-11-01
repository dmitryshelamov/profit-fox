using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Commands.UpdateBot;

public sealed class UpdateBotCommandHandler(IStrategyLibraryRepository repository, IMarketHistoryClient marketHistory, TimeProvider time)
    : IRequestHandler<UpdateBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ValidationFailedException">A setting is invalid or the exchange has no such symbol.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being saved.</exception>
    public async Task Handle(UpdateBotCommand request, CancellationToken cancellationToken)
    {
        BotSettings settings = BotRequests.Read(request.Name, request.ParameterValues, request.Category, request.Symbol, request.Interval, request.Balance);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        _ = BotRequests.Check(() => library.UpdateBot(
                request.Id,
                settings.Name,
                settings.ParameterValues,
                settings.Instrument,
                settings.Interval,
                settings.Balance,
                time.GetUtcNow()))
            ?? throw new NotFoundException($"There is no bot {request.Id}.");
        await BotRequests.EnsureListedAsync(marketHistory, settings.Instrument, cancellationToken);
        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
    }
}