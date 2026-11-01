using System.Text.Json;

using MediatR;

namespace ProfitFox.Application.Backtests.Commands.CreateBacktest;

/// <summary>A new backtest of a bot from raw settings as the user sent them; returns its id without waiting for the run.</summary>
public sealed record CreateBacktestCommand(
    Guid BotId,
    string From,
    string To,
    JsonElement? Parameters,
    string? SearchMode,
    decimal? Balance,
    decimal? LimitFeePercent,
    decimal? MarketFeePercent,
    decimal? SlippagePercent,
    string? LimitFillMode) : IRequest<Guid>;