using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Cli.Contracts.Backtests;

namespace ProfitFox.Cli.Api;

/// <summary>
///     The calls of the commands to the bench API. A refusal (any answer that is not a success) is not an exception: the answer
///     is returned with its reason for the command to print.
/// </summary>
internal sealed class BenchClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Answer<BacktestDto>> Enqueue(CreateBacktestDto backtest, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/backtests", backtest, Json, cancellation);
        return await Read<BacktestDto>(response, cancellation);
    }

    public async Task<Answer<BacktestDto>> Find(string id, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/backtests/{Uri.EscapeDataString(id)}", cancellation);
        return await Read<BacktestDto>(response, cancellation);
    }

    public async Task<Answer<BacktestDto[]>> Active(CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync("api/backtests?active=true", cancellation);
        return await Read<BacktestDto[]>(response, cancellation);
    }

    public async Task<Answer<BacktestDto[]>> Finished(int limit, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/backtests?active=false&limit={limit}", cancellation);
        return await Read<BacktestDto[]>(response, cancellation);
    }

    public async Task<Answer<object>> Cancel(string id, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.PostAsync($"api/backtests/{Uri.EscapeDataString(id)}/cancel", content: null, cancellation);
        return response.IsSuccessStatusCode
            ? new Answer<object>(response.StatusCode, new object(), "")
            : new Answer<object>(response.StatusCode, null, await Refusals.Describe(response, cancellation));
    }

    private static async Task<Answer<T>> Read<T>(HttpResponseMessage response, CancellationToken cancellation)
        where T : class
    {
        return response.IsSuccessStatusCode
            ? new Answer<T>(response.StatusCode, await response.Content.ReadFromJsonAsync<T>(Json, cancellation), "")
            : new Answer<T>(response.StatusCode, null, await Refusals.Describe(response, cancellation));
    }
}
