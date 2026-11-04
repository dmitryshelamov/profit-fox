using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;

namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Counts every HTTP attempt to Bybit REST, retries included, by endpoint, HTTP code and outcome. It sits on every client of
///     the factory and lets requests to any other address pass uncounted.
/// </summary>
internal sealed class BybitRestMetricsHandler(IMeterFactory meters, BybitSettings settings, BybitLimitHeadroom headroom) : DelegatingHandler
{
    private readonly Uri _bybit = new Uri(settings.Environment().RestBaseAddress);

    private readonly Counter<long> _requests = meters.Create("ProfitFox.Bybit").CreateCounter<long>("profitfox.bybit.rest.requests", "{request}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Uri.Compare(request.RequestUri, _bybit, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string path = request.RequestUri!.AbsolutePath;
        var endpoint = new KeyValuePair<string, object?>("bybit.endpoint", path);
        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch
        {
            _requests.Add(1, endpoint, new KeyValuePair<string, object?>("bybit.outcome", "error"));

            throw;
        }

        headroom.Observe(path, response.Headers);
        _requests.Add(
            1,
            endpoint,
            new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode),
            new KeyValuePair<string, object?>("bybit.outcome", await OutcomeAsync(response, cancellationToken)));

        return response;
    }

    private static async Task<string> OutcomeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return "rate_limited";
        }

        if (!response.IsSuccessStatusCode)
        {
            return "error";
        }

        // The SDK reads the body after the handler: it is buffered, and the stream it reads is not the one read here.
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return RetCode(body) switch
        {
            null or 0 => "ok",
            10006 => "rate_limited",
            _ => "error"
        };
    }

    /// <summary>The top-level <c>retCode</c> of an answer; none when the body is not a JSON object or has no such field.</summary>
    private static int? RetCode(byte[] body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            reader.Read();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            // Bybit writes retCode first, so the rest of a page of candles is never read.
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool found = reader.ValueTextEquals("retCode"u8);
                reader.Read();

                if (found)
                {
                    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int code) ? code : null;
                }

                reader.Skip();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}