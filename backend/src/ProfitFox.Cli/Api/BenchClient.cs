using System.Net.Http.Json;
using System.Text.Json;
using ProfitFox.Cli.Hypotheses;
using ProfitFox.Cli.Runs;

namespace ProfitFox.Cli.Api;

/// <summary>
///     The calls of the commands to the bench API. A refusal (any answer that is not a success) is not an exception: the answer
///     is returned with its reason for the command to print.
/// </summary>
internal sealed class BenchClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Answer<RunDto>> Enqueue(CreateRunDto run, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/runs", run, Json, cancellation);
        return await Read<RunDto>(response, cancellation);
    }

    public async Task<Answer<RunDto>> Find(string id, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/runs/{Uri.EscapeDataString(id)}", cancellation);
        return await Read<RunDto>(response, cancellation);
    }

    public async Task<Answer<RunDto[]>> Active(CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync("api/runs?active=true", cancellation);
        return await Read<RunDto[]>(response, cancellation);
    }

    public async Task<Answer<RunDto[]>> Finished(int limit, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/runs?active=false&limit={limit}", cancellation);
        return await Read<RunDto[]>(response, cancellation);
    }

    public async Task<Answer<object>> Cancel(string id, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await http.PostAsync($"api/runs/{Uri.EscapeDataString(id)}/cancel", content: null, cancellation);
        return response.IsSuccessStatusCode
            ? new Answer<object>(response.StatusCode, new object(), "")
            : new Answer<object>(response.StatusCode, null, await Refusals.Describe(response, cancellation));
    }

    /// <summary>
    ///     Sends the bytes of a <c>hypothesis.json</c> as they are, so that the hash of the version is that of the file.
    /// </summary>
    public async Task<Answer<HypothesisAddedDto>> AddHypothesis(byte[] text, CancellationToken cancellation)
    {
        using var content = new ByteArrayContent(text);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        using HttpResponseMessage response = await http.PostAsync("api/hypotheses", content, cancellation);
        return await Read<HypothesisAddedDto>(response, cancellation);
    }

    private static async Task<Answer<T>> Read<T>(HttpResponseMessage response, CancellationToken cancellation)
        where T : class
    {
        return response.IsSuccessStatusCode
            ? new Answer<T>(response.StatusCode, await response.Content.ReadFromJsonAsync<T>(Json, cancellation), "")
            : new Answer<T>(response.StatusCode, null, await Refusals.Describe(response, cancellation));
    }
}
