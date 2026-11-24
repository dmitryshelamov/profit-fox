using System.Text.Json;

using MediatR;

using ProfitFox.Application.Commands.Strategies.Base;
using ProfitFox.Application.Contracts.Backtests;
using ProfitFox.Application.Contracts.Backtests.Converters;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Commands.Backtests.EnqueueBacktest;

/// <summary>
///     The request is checked whole before anything is written: the version of the strategy, its graph by the rules of its
///     kind with the saved signals it points at, and the settings with the listed pairs and the clock. A backtest keeps the
///     expanded graph of the version, so a later version or the deletion of the strategy does not change it.
/// </summary>
internal sealed class EnqueueBacktestCommandHandler(
    IStrategyRepository strategies,
    ISignalRepository signals,
    IInstrumentCatalog instruments,
    IBacktestRepository backtests,
    TimeProvider time) : IRequestHandler<EnqueueBacktestCommand, BacktestInternal>
{
    /// <exception cref="StrategyLibraryException">There is no such strategy or version, or it does not read.</exception>
    /// <exception cref="BacktestRequestException">The graph of the version or a setting has an error.</exception>
    /// <exception cref="HistoryUnavailableException">The catalog of pairs did not answer.</exception>
    public async Task<BacktestInternal> Handle(EnqueueBacktestCommand request, CancellationToken cancellationToken)
    {
        StrategyDocument strategy = await VersionOf(request.Strategy, request.Version, cancellationToken);
        using var graph = JsonDocument.Parse(StrategyGraph.Write(strategy.Graph));
        StrategyGraphCheck check = await StrategyChecks.CheckAsync(signals, graph.RootElement, strategy.Kind, cancellationToken);
        List<BacktestError> errors = [.. check.Errors.Select(error => BacktestError.Of(error) with { Field = error.Field ?? "graph" })];
        string[] listed = [.. (await instruments.LinearUsdtPerpetuals(cancellationToken)).Select(pair => pair.Symbol)];
        if (check.Strategy is { } checkedStrategy)
        {
            errors.AddRange(StrategyGraph.CheckNamedPairs(checkedStrategy.Graph, listed).Select(error => BacktestError.Of(error) with { Field = error.Field ?? "graph" }));
        }

        BacktestCheck settings = BacktestRules.Check(request.Request, strategy.Graph, strategy.Kind, listed, time.GetUtcNow());
        errors.AddRange(settings.Errors);
        if (errors.Count > 0 || check.Strategy is null || settings.Settings is null)
        {
            throw new BacktestRequestException(errors);
        }

        IReadOnlyList<BacktestSet> sets = BacktestGrid.Of(settings.Settings);
        var backtest = new Backtest
        {
            Id = Guid.NewGuid().ToString("N"),
            StrategyId = strategy.Id,
            StrategyName = strategy.Name,
            StrategyVersion = strategy.Version,
            Kind = strategy.Kind,
            Graph = check.Strategy.Graph,
            Settings = settings.Settings,
            Sets = [.. sets.Select(set => new BacktestSetOutcome(set, Numbers: null))],
            SetCount = sets.Count
        };
        return (await backtests.EnqueueAsync(backtest, cancellationToken)).ToInternal();
    }

    private async Task<StrategyDocument> VersionOf(string name, int? version, CancellationToken cancellationToken)
    {
        if (version is not { } number)
        {
            return await StrategyChecks.ReadAsync(strategies, name, cancellationToken);
        }

        if (!SignalNames.IsValid(name) || await strategies.ReadVersionAsync(name, number, cancellationToken) is not { } read)
        {
            throw new StrategyLibraryException(StrategyProblem.NotFound, $"there is no version {number} of the strategy {name}");
        }

        return read.Strategy ?? throw new StrategyLibraryException(StrategyProblem.Unreadable, string.Join("; ", read.Errors));
    }
}
