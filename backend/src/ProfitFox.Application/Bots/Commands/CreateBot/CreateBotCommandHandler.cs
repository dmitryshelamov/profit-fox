using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;

namespace ProfitFox.Application.Bots.Commands.CreateBot;

public sealed class CreateBotCommandHandler(
    IStrategyLibraryRepository repository,
    IExchangeAccountRepository accounts,
    IMarketHistoryClient marketHistory,
    TimeProvider time)
    : IRequestHandler<CreateBotCommand, Guid>
{
    /// <summary>The symbol is checked on the exchange last, once everything else is valid.</summary>
    /// <exception cref="ValidationFailedException">A setting is invalid, the account does not fit or the exchange has no such symbol.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being saved.</exception>
    public async Task<Guid> Handle(CreateBotCommand request, CancellationToken cancellationToken)
    {
        BotSettings settings = BotRequests.Read(request.Name, request.ParameterValues, request.Category, request.Symbol, request.Interval, request.Balance);
        BotAccount? account = await BotRequests.AccountAsync(accounts, request.ExchangeAccountId, cancellationToken);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        Bot bot = BotRequests.Check(() => library.AddBot(
            Guid.NewGuid(),
            settings.Name,
            request.StrategyId,
            settings.ParameterValues,
            settings.Instrument,
            settings.Interval,
            settings.Balance,
            time.GetUtcNow(),
            account));
        await BotRequests.EnsureListedAsync(marketHistory, settings.Instrument, cancellationToken);
        await BotRequests.SaveAsync(repository, library, cancellationToken);

        return bot.Id;
    }
}