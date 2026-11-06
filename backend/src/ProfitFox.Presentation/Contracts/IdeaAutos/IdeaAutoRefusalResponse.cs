using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>
///     A refusal: when, which auto in which mode, the pair and the setup, interval, type and side of the idea as codes, every reason as a code, and what the
///     first reason is about. Numbers are decimal strings.
/// </summary>
public sealed record IdeaAutoRefusalResponse(
    Guid Id,
    DateTimeOffset At,
    Guid AutoId,
    [property: Required] string AutoName,
    [property: Required] string Mode,
    [property: Required] string Category,
    [property: Required] string Symbol,
    [property: Required] string Setup,
    [property: Required] string Interval,
    [property: Required] string Type,
    [property: Required] string Side,
    [property: Required] IReadOnlyList<string> Reasons,
    RefusalDetailResponse? Detail)
{
    public static IdeaAutoRefusalResponse Of(IdeaAutoRefusalView view)
    {
        IdeaAutoRefusal refusal = view.Refusal;

        return new IdeaAutoRefusalResponse(
            refusal.Id,
            refusal.At,
            refusal.AutoId,
            view.AutoName,
            refusal.Mode.ToCode(),
            refusal.Instrument.Category.ToCode(),
            refusal.Instrument.Symbol,
            refusal.Setup.ToCode(),
            refusal.Interval.ToCode(),
            refusal.Type.ToCode(),
            refusal.Side.ToCode(),
            [.. refusal.Reasons.Select(IdeaTradeCodes.ToCode)],
            RefusalDetailResponse.Of(refusal.Detail));
    }
}

/// <summary>What the first reason of a refusal is about: a number and its limit, or the trade that holds the pair.</summary>
public sealed record RefusalDetailResponse(string? Actual, string? Limit, PairHolderResponse? Holder)
{
    public static RefusalDetailResponse? Of(RefusalDetail? detail)
    {
        return detail is null
            ? null
            : new RefusalDetailResponse(
                detail.Actual is { } actual ? IdeaAutoResponse.Text(actual) : null,
                detail.Limit is { } limit ? IdeaAutoResponse.Text(limit) : null,
                PairHolderResponse.Of(detail.Holder));
    }
}

/// <summary>
///     The open trade that holds the pair: <c>same_auto</c>, <c>other_auto</c> (its name, and whether it stands above in the list) or <c>manual</c>, a paper
///     trade or a real one, on which account (none for a trade of the auto itself) and since when.
/// </summary>
public sealed record PairHolderResponse([property: Required] string Kind, string? AutoName, bool Above, bool Paper, string? AccountName, DateTimeOffset Since)
{
    public static PairHolderResponse? Of(PairHolder? holder)
    {
        return holder is null
            ? null
            : new PairHolderResponse(
                holder.Kind switch
                {
                    PairHolderKind.SameAuto => "same_auto",
                    PairHolderKind.OtherAuto => "other_auto",
                    _ => "manual"
                },
                holder.AutoName,
                holder.Above,
                holder.Paper,
                holder.AccountName,
                holder.Since);
    }
}

/// <summary>A page of the refusals: its number from 1 (the last page when the one asked for is past the end), its size and the count of all of them.</summary>
public sealed record IdeaAutoRefusalsPageResponse([property: Required] IReadOnlyList<IdeaAutoRefusalResponse> Items, int Page, int PageSize, int Total);