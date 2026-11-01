using ProfitFox.Domain.ValueObjects.Backtests;

namespace ProfitFox.Domain.Exceptions;

/// <summary>A backtest cannot be created as asked; <see cref="Errors" /> lists every problem.</summary>
public sealed class BacktestValidationException(IReadOnlyList<BacktestError> errors) : Exception("The backtest is invalid.")
{
    public IReadOnlyList<BacktestError> Errors { get; } = errors;
}