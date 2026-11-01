using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Commands.CreateBot;

public sealed class CreateBotCommandHandler(IStrategyLibraryRepository repository, IMarketHistoryClient marketHistory, TimeProvider time)
    : IRequestHandler<CreateBotCommand, Guid>
{
    /// <summary>The symbol is checked on the exchange last, once everything else is valid.</summary>
    /// <exception cref="ValidationFailedException">A setting is invalid or the exchange has no such symbol.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being saved.</exception>
    public async Task<Guid> Handle(CreateBotCommand request, CancellationToken cancellationToken)
    {
        BotSettings settings = BotRequests.Read(request.Name, request.ParameterValues, request.Category, request.Symbol, request.Interval, request.Balance);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        Bot bot = BotRequests.Check(() => library.AddBot(
            Guid.NewGuid(),
            settings.Name,
            request.StrategyId,
            settings.ParameterValues,
            settings.Instrument,
            settings.Interval,
            settings.Balance,
            time.GetUtcNow()));
        await BotRequests.EnsureListedAsync(marketHistory, settings.Instrument, cancellationToken);
        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));

        return bot.Id;
    }
}