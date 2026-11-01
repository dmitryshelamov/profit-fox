using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Backtests.Commands.CreateBacktest;

public sealed class CreateBacktestCommandHandler(
    IStrategyLibraryRepository library,
    IBacktestRepository backtests,
    IInstrumentRulesProvider exchange,
    BacktestSettings settings,
    EngineVersion engine,
    TimeProvider time) : IRequestHandler<CreateBacktestCommand, Guid>
{
    /// <summary>The rules of the instrument are asked from the exchange and kept with the backtest, so a later recount uses the same.</summary>
    /// <exception cref="ValidationFailedException">A setting is invalid.</exception>
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="ExchangeException">The exchange did not give the rules of the instrument.</exception>
    public async Task<Guid> Handle(CreateBacktestCommand request, CancellationToken cancellationToken)
    {
        (DateTimeOffset from, DateTimeOffset to, IReadOnlyDictionary<string, Domain.ValueObjects.Backtests.ParameterSpec>? parameters, Domain.ValueObjects.Backtests.SimulationRequest? simulation) = BacktestRequests.Read(
            request.From,
            request.To,
            request.Parameters,
            request.SearchMode,
            request.Balance,
            request.LimitFeePercent,
            request.MarketFeePercent,
            request.SlippagePercent,
            request.LimitFillMode);
        Bot bot = await library.GetBotAsync(request.BotId, cancellationToken) ?? throw new NotFoundException($"There is no bot {request.BotId}.");
        int number = (await library.GetLatestVersionNumberAsync(bot.StrategyId, cancellationToken))!.Value;
        StrategyVersion latest = (await library.GetVersionAsync(bot.StrategyId, number, cancellationToken))!;
        InstrumentRules rules = await exchange.GetRulesAsync(bot.Instrument, cancellationToken);
        Backtest backtest = BacktestRequests.Check(() =>
            Backtest.Create(Guid.NewGuid(), bot, latest, from, to, parameters, simulation, settings.Policy(), rules, engine.Number, time.GetUtcNow()));
        await backtests.AddAsync(backtest, cancellationToken);

        return backtest.Id;
    }
}