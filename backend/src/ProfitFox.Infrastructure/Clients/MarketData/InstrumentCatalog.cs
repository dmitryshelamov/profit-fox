using System.Text.Json;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Clients.MarketData;

/// <summary>
///     The set of pairs of the bench (spec market-history): linear Bybit perpetual contracts quoted in USDT that are listed
///     now, from <c>GET api/instruments/bybit</c> of the market-data service. Delisted pairs are not there, so every report
///     warns about survivors.
/// </summary>
public sealed class InstrumentCatalog(HttpClient http) : IInstrumentCatalog
{
    public async Task<IReadOnlyList<Instrument>> LinearUsdtPerpetuals(CancellationToken cancellation)
    {
        string json;
        try
        {
            json = await http.GetStringAsync("api/instruments/bybit", cancellation);
        }
        catch (HttpRequestException exception)
        {
            throw new HistoryUnavailableException("The instrument catalog of the market-data service did not answer.", exception);
        }

        return Parse(json);
    }

    /// <summary>
    ///     The bench pairs of a catalog answer, ordered by symbol.
    /// </summary>
    public static IReadOnlyList<Instrument> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        List<Instrument> instruments = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            JsonElement listing = item.GetProperty("listing");
            bool delisted = item.TryGetProperty("delistedAt", out JsonElement delistedAt) && delistedAt.ValueKind != JsonValueKind.Null;
            if (!delisted
                && Text(listing, "category") == "Linear"
                && Text(listing, "contractType") == "LinearPerpetual"
                && Text(listing, "quoteAsset") == "USDT"
                && Text(listing, "status") == "Trading")
            {
                instruments.Add(new Instrument(item.GetProperty("id").GetString()!, Text(listing, "symbol")!));
            }
        }

        return [.. instruments.OrderBy(instrument => instrument.Symbol, StringComparer.Ordinal)];
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
