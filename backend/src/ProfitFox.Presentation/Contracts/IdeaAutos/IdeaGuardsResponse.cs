using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.IdeaAutos;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>
///     The guards over the real trades: the open trades against their limit and the risk of them against theirs, the sum of the day since 00:00 UTC
///     against the daily stop (with the time it was reached and the time the autos enter again, once it holds), and the open longs and shorts against the
///     limit of one side. A guard that is off has no limit. Numbers are decimal strings.
/// </summary>
public sealed record IdeaGuardsResponse(
    int OpenTrades,
    int MaxOpenTrades,
    [property: Required] string OpenRisk,
    [property: Required] string MaxTotalRisk,
    [property: Required] string DaySum,
    string? DailyLossLimit,
    DateTimeOffset? TriggeredAt,
    DateTimeOffset? ResumesAt,
    int OpenLongs,
    int OpenShorts,
    int? MaxSameDirection)
{
    public static IdeaGuardsResponse Of(IdeaGuardsState state)
    {
        return new IdeaGuardsResponse(
            state.OpenTrades,
            state.MaxOpenTrades,
            Text(state.OpenRisk),
            Text(state.MaxTotalRisk),
            Text(state.DailyLoss.Sum),
            state.DailyLoss.Limit is { } limit ? Text(limit) : null,
            state.DailyLoss.TriggeredAt,
            state.DailyLoss.ResumesAt,
            state.OpenLongs,
            state.OpenShorts,
            state.MaxSameDirection);
    }

    internal static string Text(decimal value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}