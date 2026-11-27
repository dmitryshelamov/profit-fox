using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Application.Services.Ideas;

/// <summary>
///     An enabled strategy on one of its timeframes, as the scanner runs it: its latest version, checked and expanded.
/// </summary>
public sealed record StrategyRunSpec(Guid Id, string Name, int Version, TimeSpan Timeframe, TradingStrategy Strategy)
{
    public string Key => $"s:{Id}:{Version}:{Timeframe.Ticks}";
}

/// <summary>
///     An enabled label: its signal on its latest version and the timeframe of the label.
/// </summary>
public sealed record LabelRunSpec(Guid SignalId, string Text, int Version, TimeSpan Timeframe, Signal Signal)
{
    public string Key => $"l:{SignalId}:{Version}:{Timeframe.Ticks}:{Text}";
}
