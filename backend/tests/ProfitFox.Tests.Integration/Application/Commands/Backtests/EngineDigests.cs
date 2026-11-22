using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     The digests of the results of a reference strategy (change add-strategy-backtest, design.md, decision 7, step 3):
///     the rows of the orders with their trades, the random entries with theirs, the weekly means and the numbers of the
///     set; and apart, the trades of the orders alone and of the random entries alone, which the transition from engine
///     version 2 keeps.
/// </summary>
internal static class EngineDigests
{
    public static string Of(IReadOnlyList<EventRow> rows, SetNumbers numbers)
    {
        var text = new StringBuilder();
        foreach (EventRow row in rows)
        {
            Line(text, "order", row.Instrument, row.Moment.ToUnixTimeMilliseconds(), row.Block, row.Status, row.RandomValid, row.NetAmount);
            Fields(text, "order-fields", row.Event);
            Fields(text, "random-mean", row.RandomMean);
            Outcome(text, "order-outcome", row.Outcome);
            foreach (RandomTrade random in row.Randoms)
            {
                Line(text, "random", random.Entry.ToUnixTimeMilliseconds());
                Outcome(text, "random-outcome", random.Outcome);
            }
        }

        foreach ((long week, double mean, int orders) in numbers.WeekMeans())
        {
            Line(text, "week", week, mean, orders);
        }

        var set = SetSummary.Of(numbers);
        Line(text, "set", set.Orders, set.Trades, set.PairsWithTrades, set.Mean, set.Amount, set.WinShare, set.Skipped, set.OpenCycles, set.OpenAmount);
        Line(text, "excluded", set.Excluded.Plan, set.Excluded.Coverage, set.Excluded.Funding, set.Excluded.NoTrade, set.Excluded.Period);
        Line(text, "difference", set.Difference?.Mean, set.Difference?.Weeks, set.Difference?.T);
        return Hash(text);
    }

    /// <summary>
    ///     The trades of the orders: the moment, the status and the outcome of each.
    /// </summary>
    public static string Orders(IReadOnlyList<EventRow> rows)
    {
        var text = new StringBuilder();
        foreach (EventRow row in rows)
        {
            Line(text, "order", row.Instrument, row.Moment.ToUnixTimeMilliseconds(), row.Status);
            Outcome(text, "order-outcome", row.Outcome);
        }

        return Hash(text);
    }

    /// <summary>
    ///     The random entries of the orders with their trades.
    /// </summary>
    public static string Randoms(IReadOnlyList<EventRow> rows)
    {
        var text = new StringBuilder();
        foreach (EventRow row in rows)
        {
            foreach (RandomTrade random in row.Randoms)
            {
                Line(text, "random", row.Instrument, random.Entry.ToUnixTimeMilliseconds());
                Outcome(text, "random-outcome", random.Outcome);
            }
        }

        return Hash(text);
    }

    private static string Hash(StringBuilder text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Fields(StringBuilder text, string name, TradeFields? fields)
    {
        Line(text, name, fields?.Y, fields?.Gross, fields?.Fees, fields?.Slippage, fields?.Funding, fields?.Net);
    }

    private static void Outcome(StringBuilder text, string name, TradeOutcome? outcome)
    {
        Line(text, name, outcome?.Status, outcome?.ExitReason, outcome?.Ambiguous, outcome?.EntryTime?.ToUnixTimeMilliseconds(),
            outcome?.ExitTime?.ToUnixTimeMilliseconds(), outcome?.EntryPrice, outcome?.ExitPrice, outcome?.Gross, outcome?.Fees,
            outcome?.Slippage, outcome?.Funding, outcome?.Net, outcome?.R);
    }

    // Numbers are cut to 12 significant digits so that the last bit of a platform's math does not change the digest.
    private static void Line(StringBuilder text, params object?[] values)
    {
        text.AppendJoin(';', values.Select(value => value switch
        {
            null => "-",
            double number => number.ToString("G12", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        }));
        text.Append('\n');
    }
}
