using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A backtest was refused when it was asked, with every error of the request; nothing was queued.
/// </summary>
public sealed class BacktestRequestException(IReadOnlyList<BacktestError> errors)
    : Exception(string.Join("; ", errors.Select(error => error.Reason)))
{
    public IReadOnlyList<BacktestError> Errors { get; } = errors;
}
