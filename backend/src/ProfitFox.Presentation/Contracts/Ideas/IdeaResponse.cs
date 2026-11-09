using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     A trade idea with its levels and where it stands, its own numbers (<see cref="Numbers" />), and the statistics of its bucket over 90 days
///     (<see cref="Bucket" />) and over the last 30 (<see cref="Recent" />), none while the bucket has no case. <see cref="BreakEven" /> is the share of targets the idea breaks even at after the fees if it
///     never expires, none when the target does not cover them.
///     <see cref="ProfitPerUsdt" />, <see cref="LossPerUsdt" /> and <see cref="RiskPerUsdt" /> are its money on 1 USDT of the amount of a deal,
///     after the fees. <see cref="RiskReward" /> is the ratio of the prices, <see cref="NetRiskReward" /> the profit over the loss after the fees. <see cref="Tick" /> is the price step of the pair, none for an idea made before it was kept. Numbers are decimal strings; <see cref="Move" /> in ATR, <see cref="VolumeRatio" /> in medians of volume.
///     <see cref="Features" /> are the codes of the features of the market the idea fired with (<c>day_drop</c>, <c>btc_drop</c>,
///     <c>high_funding</c>), none of them for an idea without.
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
    string NetRiskReward,
    string? Tick,
    string ProfitPerUsdt,
    string LossPerUsdt,
    string RiskPerUsdt,
    string Status,
    DateTimeOffset StatusAt,
    DateTimeOffset? EnteredAt,
    string? ExitPrice,
    string Move,
    string VolumeRatio,
    string? BreakEven,
    IdeaNumbersResponse Numbers,
    BucketStatisticsResponse? Bucket,
    BucketStatisticsResponse? Recent,
    IReadOnlyList<string> Features)
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
            Text(view.Shares.NetRiskReward),
            track.Tick?.ToString(CultureInfo.InvariantCulture),
            Text(view.Shares.ProfitPerUsdt),
            Text(view.Shares.LossPerUsdt),
            Text(view.Shares.RiskPerUsdt),
            track.Status.ToCode(),
            track.StatusAt,
            track.EnteredAt,
            track.ExitPrice?.ToString(CultureInfo.InvariantCulture),
            Text(idea.Move),
            Text(idea.VolumeRatio),
            view.Shares.BreakEven?.ToString(CultureInfo.InvariantCulture),
            IdeaNumbersResponse.Of(view.Numbers),
            BucketStatisticsResponse.Of(view.Bucket),
            BucketStatisticsResponse.Of(view.Recent),
            IdeaCodes.Codes(idea.Features));
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}