using System.Diagnostics;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.TradeFlow;

/// <summary>
///     One pass of the rebuild of closed days from the daily archive (decision 8 of collect-trade-flow): the days UTC of every pair in
///     <c>[today − HistoryDays, yesterday]</c> storage has no record of, the freshest first, each once the archive of the day has come out
///     (<see cref="TradeFlowSettings.ArchiveNotBefore" /> of the next day). A file replaces every minute of the pair in its day and records the
///     day rebuilt; a day without a file stays to the next pass, and <see cref="MissingAfter" /> after its end it is recorded missing and left
///     as it is. One file at a time.
/// </summary>
public sealed class TradeFlowRebuild(
    ITradeArchive archive,
    ITradeFlowMinuteRepository minutes,
    ITradeFlowDayRepository days,
    TradeFlowSettings settings,
    WorkerIdentity identity,
    ITradeFlowJournal journal,
    TimeProvider time)
{
    /// <summary>How long after the end of a day the archive having no file of a pair for it is final.</summary>
    public static readonly TimeSpan MissingAfter = TimeSpan.FromDays(3);

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    /// <exception cref="TradeFlowLeaseLostException">Another collector took the trade flow.</exception>
    public async Task RunAsync(IReadOnlyCollection<Instrument> pairs, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        DateTimeOffset from = today.AddDays(-settings.HistoryDays);
        HashSet<(Instrument, DateTimeOffset)> recorded = [.. (await days.GetAsync(from, today, cancellationToken)).Select(day => (day.Instrument, day.Day))];
        IEnumerable<(Instrument Instrument, DateTimeOffset Day)> queue =
            from day in Enumerable.Range(1, settings.HistoryDays).Select(back => today.AddDays(-back))
            where day + Day + settings.ArchiveNotBefore <= now
            from instrument in pairs
            where !recorded.Contains((instrument, day))
            select (instrument, day);

        foreach ((Instrument instrument, DateTimeOffset day) in queue)
        {
            await RebuildAsync(instrument, day, cancellationToken);
        }
    }

    private async Task RebuildAsync(Instrument instrument, DateTimeOffset day, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        var read = new Dictionary<DateTimeOffset, TradeFlowMinute>();
        var trades = 0;

        bool found = await archive.ReadDayAsync(
            instrument,
            day,
            trade =>
            {
                var minute = TradeFlowMinute.Start(trade);
                read[minute.OpenTime] = read.TryGetValue(minute.OpenTime, out TradeFlowMinute? stored) ? stored.Add(trade) : minute;
                trades++;
            },
            cancellationToken);

        if (found)
        {
            if (!await minutes.ReplaceDayAsync(identity.Id, instrument, day, [.. read.Values], cancellationToken))
            {
                throw new TradeFlowLeaseLostException();
            }

            await days.SaveAsync(new TradeFlowDay(instrument, day, TradeFlowDayStatus.Rebuilt, time.GetUtcNow()), cancellationToken);
            journal.Rebuilt(instrument, day, trades, Stopwatch.GetElapsedTime(started));
        }
        else if (day + Day + MissingAfter <= time.GetUtcNow())
        {
            await days.SaveAsync(new TradeFlowDay(instrument, day, TradeFlowDayStatus.Missing, time.GetUtcNow()), cancellationToken);
        }
    }
}