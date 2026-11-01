using ProfitFox.Domain.Entities.Backtests.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <param name="Field"></param>
/// <param name="Parameter">The strategy parameter of a <see cref="BacktestField.Parameter" /> error.</param>
/// <param name="Reason"></param>
public sealed record BacktestError(BacktestField Field, string? Parameter, string Reason);