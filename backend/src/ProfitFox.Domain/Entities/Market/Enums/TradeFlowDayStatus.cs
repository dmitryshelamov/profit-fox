namespace ProfitFox.Domain.Entities.Market.Enums;

/// <summary>
///     What the daily archive gave for a day of a pair: <see cref="Rebuilt" />, its minutes replaced by those of the archive, or
///     <see cref="Missing" />, no file for good, its minutes left as they are.
/// </summary>
public enum TradeFlowDayStatus
{
    Rebuilt,
    Missing
}