using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A trade idea with its levels and where it stands, and the statistics of its bucket over 90 days (<see cref="Bucket" />) and over the last
///     30 (<see cref="Recent" />), none while the bucket has no case. <see cref="Baseline" /> is the bucket's, or 1 / (1 + R:R) of the idea.
///     <see cref="Tick" /> is the price step of the pair, none for an idea made before it was kept. Numbers are decimal strings; <see cref="Move" /> in ATR, <see cref="VolumeRatio" /> in medians of volume.
/// </summary>
public sealed record IdeaResponse(
    Guid Id,
    string Category,
    string Symbol,
    string Setup,
    string Interval,
    string Type,
    string Side,
    DateTimeOffset FiredAt,
    string Entry,
    string Stop,
    string Target,
    string RiskReward,
    string? Tick,
    string Status,
    DateTimeOffset StatusAt,
    DateTimeOffset? EnteredAt,
    string? ExitPrice,
    string Move,
    string VolumeRatio,
    string Baseline,
    BucketStatisticsResponse? Bucket,
    BucketStatisticsResponse? Recent)
{
    public static IdeaResponse Of(IdeaView view)
    {
        Idea idea = view.Idea;
        IdeaTrack track = idea.Track;

        return new IdeaResponse(
            idea.Id,
            idea.Instrument.Category.ToCode(),
            idea.Instrument.Symbol,
            idea.Setup.ToCode(),
            idea.Interval.ToCode(),
            track.Plan.Type.ToCode(),
            track.Plan.Side.ToCode(),
            track.FiredAt,
            Text(track.Plan.Entry),
            Text(track.Plan.Stop),
            Text(track.Plan.Target),
            Text(track.Plan.RiskReward),
            track.Tick?.ToString(CultureInfo.InvariantCulture),
            track.Status.ToCode(),
            track.StatusAt,
            track.EnteredAt,
            track.ExitPrice?.ToString(CultureInfo.InvariantCulture),
            Text(idea.Move),
            Text(idea.VolumeRatio),
            Text(view.Baseline),
            BucketStatisticsResponse.Of(view.Bucket),
            BucketStatisticsResponse.Of(view.Recent));
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}