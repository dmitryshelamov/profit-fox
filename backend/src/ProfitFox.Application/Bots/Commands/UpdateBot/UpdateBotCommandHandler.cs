using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Bots;

namespace ProfitFox.Application.Bots.Commands.UpdateBot;

public sealed class UpdateBotCommandHandler(
    IStrategyLibraryRepository repository,
    IExchangeAccountRepository accounts,
    IMarketHistoryClient marketHistory,
    TimeProvider time)
    : IRequestHandler<UpdateBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ValidationFailedException">A setting is invalid, the account does not fit or the exchange has no such symbol.</exception>
    /// <exception cref="ExchangeException">The exchange could not confirm the symbol.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being saved.</exception>
    public async Task Handle(UpdateBotCommand request, CancellationToken cancellationToken)
    {
        BotSettings settings = BotRequests.Read(request.Name, request.ParameterValues, request.Category, request.Symbol, request.Interval, request.Balance);
        BotAccount? account = await BotRequests.AccountAsync(accounts, request.ExchangeAccountId, cancellationToken);
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);
        _ = BotRequests.Check(() => library.UpdateBot(
                request.Id,
                settings.Name,
                settings.ParameterValues,
                settings.Instrument,
                settings.Interval,
                settings.Balance,
                time.GetUtcNow(),
                account))
            ?? throw new NotFoundException($"There is no bot {request.Id}.");
        await BotRequests.EnsureListedAsync(marketHistory, settings.Instrument, cancellationToken);
        await BotRequests.SaveAsync(repository, library, cancellationToken);
    }
}