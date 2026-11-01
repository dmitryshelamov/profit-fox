namespace ProfitFox.Domain.Exceptions;

/// <summary>A finished backtest cannot be changed any more.</summary>
public sealed class BacktestFinishedException() : Exception("The backtest is finished.");