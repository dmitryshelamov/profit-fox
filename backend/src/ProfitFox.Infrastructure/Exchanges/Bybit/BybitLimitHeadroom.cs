using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net.Http.Headers;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The headroom of an account on a Bybit endpoint, from the <c>X-Bapi-Limit</c> headers: the last limit and the lowest remainder
///     since the previous collection, over all accounts. One for the process, as its gauges are.
/// </summary>
/// <remarks>
///     Collecting the remainder takes it: the exporter collects once per export, so the export period is the period of the lowest.
///     A second reader of the meter, such as another exporter or a <see cref="MeterListener" />, would split the periods between them.
/// </remarks>
internal sealed class BybitLimitHeadroom
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, int> _limits = [];
    private readonly Dictionary<string, int> _lowest = [];

    public BybitLimitHeadroom(IMeterFactory meters)
    {
        Meter meter = meters.Create("ProfitFox.Bybit");
        meter.CreateObservableGauge("profitfox.bybit.rest.limit", Limits, "{request}");
        meter.CreateObservableGauge("profitfox.bybit.rest.limit_remaining", TakeRemaining, "{request}");
    }

    public void Observe(string endpoint, HttpResponseHeaders headers)
    {
        if (Header(headers, "X-Bapi-Limit") is not { } limit || Header(headers, "X-Bapi-Limit-Status") is not { } remaining)
        {
            return;
        }

        lock (_lock)
        {
            _limits[endpoint] = limit;
            _lowest[endpoint] = _lowest.TryGetValue(endpoint, out int lowest) ? Math.Min(lowest, remaining) : remaining;
        }
    }

    public IEnumerable<Measurement<int>> Limits()
    {
        lock (_lock)
        {
            return Measurements(_limits);
        }
    }

    public IEnumerable<Measurement<int>> TakeRemaining()
    {
        lock (_lock)
        {
            Measurement<int>[] lowest = Measurements(_lowest);
            _lowest.Clear();

            return lowest;
        }
    }

    private static int? Header(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out IEnumerable<string>? values) && values.ToArray() is [string single] && int.TryParse(single, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static Measurement<int>[] Measurements(Dictionary<string, int> values)
    {
        return [.. values.Select(entry => new Measurement<int>(entry.Value, new KeyValuePair<string, object?>("bybit.endpoint", entry.Key)))];
    }
}