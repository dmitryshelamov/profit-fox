using System.Diagnostics.Metrics;
using System.Globalization;

using Grpc.Core;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The metrics of the calls to T-Invest on the meter <c>ProfitFox.TInvest</c>: every unary call sent counts once as
///     <c>profitfox.tinvest.requests</c> by group, method and outcome (<c>ok</c>, <c>rate_limited</c>, <c>error</c>); the headers
///     <c>x-ratelimit-limit</c> and <c>x-ratelimit-remaining</c> of a group give the gauges <c>profitfox.tinvest.limit</c> (the last
///     limit) and <c>profitfox.tinvest.limit_remaining</c> (the lowest remainder since the previous collection, which takes it). One for the process.
/// </summary>
internal sealed class TInvestMetrics
{
    public const string MeterName = "ProfitFox.TInvest";

    private readonly Counter<long> _requests;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<TInvestMethodGroup, int> _limits = [];
    private readonly Dictionary<TInvestMethodGroup, int> _lowest = [];

    public TInvestMetrics(IMeterFactory meters)
    {
        Meter meter = meters.Create(MeterName);
        _requests = meter.CreateCounter<long>("profitfox.tinvest.requests", "{request}");
        meter.CreateObservableGauge("profitfox.tinvest.limit", Limits, "{request}");
        meter.CreateObservableGauge("profitfox.tinvest.limit_remaining", TakeRemaining, "{request}");
    }

    public void Count(TInvestMethodGroup group, string method, string outcome)
    {
        _requests.Add(
            1,
            new KeyValuePair<string, object?>("group", group.ToCode()),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>Reads the limit headers of an answer, if it carries both.</summary>
    public void Observe(TInvestMethodGroup group, Metadata headers)
    {
        if (Header(headers, "x-ratelimit-limit") is not { } limit || Header(headers, "x-ratelimit-remaining") is not { } remaining)
        {
            return;
        }

        lock (_lock)
        {
            _limits[group] = limit;
            _lowest[group] = _lowest.TryGetValue(group, out int lowest) ? Math.Min(lowest, remaining) : remaining;
        }
    }

    private IEnumerable<Measurement<int>> Limits()
    {
        lock (_lock)
        {
            return Measurements(_limits);
        }
    }

    private IEnumerable<Measurement<int>> TakeRemaining()
    {
        lock (_lock)
        {
            Measurement<int>[] lowest = Measurements(_lowest);
            _lowest.Clear();

            return lowest;
        }
    }

    private static int? Header(Metadata headers, string name)
    {
        return headers.GetValue(name) is { } value && int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private static Measurement<int>[] Measurements(Dictionary<TInvestMethodGroup, int> values)
    {
        return [.. values.Select(entry => new Measurement<int>(entry.Value, new KeyValuePair<string, object?>("group", entry.Key.ToCode())))];
    }
}