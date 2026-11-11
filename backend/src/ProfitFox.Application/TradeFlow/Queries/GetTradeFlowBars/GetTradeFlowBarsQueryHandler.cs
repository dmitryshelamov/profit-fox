using MediatR;

using ProfitFox.Application.Market;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.TradeFlow.Queries.GetTradeFlowBars;

/// <summary>
///     The bars of the trade flow of a pair from storage (decision 10 of collect-trade-flow): <c>from</c> goes down to the start of its bar, the end is
///     cut to the start of the current minute, which only the hub gives, at most <see cref="MaxBars" /> bars; with the first stored minute of the
///     pair and the breaks of the stream in the period out of the days the archive rebuilt for the pair.
/// </summary>
public sealed class GetTradeFlowBarsQueryHandler(ITradeFlowMinuteRepository minutes, ITradeFlowDayRepository days, IStreamBreakRepository breaks, TimeProvider time)
    : IRequestHandler<GetTradeFlowBarsQuery, GetTradeFlowBarsResult>
{
    public const int MaxBars = 1000;

    private const string Stream = "trades";

    private static readonly Interval[] Intervals = [Interval.OneMinute, Interval.FiveMinutes, Interval.FifteenMinutes, Interval.OneHour, Interval.FourHours, Interval.OneDay];

    public async Task<GetTradeFlowBarsResult> Handle(GetTradeFlowBarsQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!MarketDataCodes.TryParseCategory(request.Category, out MarketCategory category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }
        else if (category == MarketCategory.Moex)
        {
            return new GetTradeFlowBarsResult(default, null, null, [], NotFound: true, errors);
        }

        if (!MarketDataCodes.TryParseInterval(category, request.Interval ?? string.Empty, out Interval interval) || !Intervals.Contains(interval))
        {
            errors["interval"] = "Interval must be one of 1, 5, 15, 60, 240 and D.";
        }

        if (!ApiTimestamps.TryParse(request.From, out DateTimeOffset from))
        {
            errors["from"] = ApiTimestamps.Invalid("from");
        }

        bool toIsTime = ApiTimestamps.TryParse(request.To, out DateTimeOffset to);

        if (!toIsTime)
        {
            errors["to"] = ApiTimestamps.Invalid("to");
        }

        // An unparsed `from` stays at its minimum and never fails the period check.
        if (toIsTime && from >= to)
        {
            errors["from"] = "from must be earlier than to.";
        }

        DateTimeOffset start = interval.CandleOpen(from);
        DateTimeOffset current = Interval.OneMinute.CandleOpen(time.GetUtcNow());
        DateTimeOffset end = to.ToUniversalTime() < current ? to.ToUniversalTime() : current;

        if (errors.Count == 0 && interval.CandleCount(start, end, MaxBars) > MaxBars)
        {
            errors["from"] = $"The period must not be longer than {MaxBars} bars.";
        }

        if (errors.Count > 0)
        {
            return new GetTradeFlowBarsResult(interval, null, null, [], NotFound: false, errors);
        }

        var instrument = new Instrument(category, request.Symbol);
        IReadOnlyList<TradeFlowMinute> bars = await minutes.GetBarsAsync(instrument, interval, start, end, cancellationToken);
        DateTimeOffset? since = await minutes.FirstAsync(instrument, cancellationToken);
        IReadOnlyList<StreamBreak> lost = await breaks.GetAsync(Stream, start, end, cancellationToken);
        IReadOnlyList<TradeFlowDay> recorded = await days.GetAsync(instrument, Interval.OneDay.CandleOpen(start), end, cancellationToken);
        DateTimeOffset[] rebuilt = [.. recorded.Where(day => day.Status == TradeFlowDayStatus.Rebuilt).Select(day => day.Day)];

        return new GetTradeFlowBarsResult(interval, bars, since, TradeFlowGaps.Of(lost, rebuilt, start, end), NotFound: false, errors);
    }
}