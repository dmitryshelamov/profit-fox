using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Npgsql;

using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Tests.Integration.Worker;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     Strategies and backtests through the API for the tests: <c>funding-short</c>, the strategy of H0001 written out, and
///     the ladder; a backtest asked with a JSON body and waited for.
/// </summary>
internal static class Backtests
{
    public static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The end of history of the tests: the end of the four weeks of the fake market-data service.
    /// </summary>
    public const string End = "2026-02-02";

    public static JsonObject FundingShort(string after = "4h")
    {
        JsonObject graph = JsonNode.Parse(BacktestGenerator.FundingShort)!.AsObject();
        graph["nodes"]!.AsArray().Single(node => (string)node!["id"]! == "close-after")!["inputs"] = new JsonObject { ["after"] = after };
        return graph;
    }

    /// <summary>
    ///     <c>funding-short</c> whose entry also asks the close of the pair <paramref name="symbol" /> to be above zero: the
    ///     node <c>btc</c> of the kind <c>pair-candle</c> (change add-ideas-and-market).
    /// </summary>
    public static JsonObject FundingShortWithPair(string symbol)
    {
        JsonObject graph = FundingShort();
        JsonArray nodes = graph["nodes"]!.AsArray();
        nodes.Add(JsonNode.Parse($$"""{ "id": "btc", "kind": "pair-candle", "settings": { "symbol": "{{symbol}}" } }"""));
        nodes.Add(JsonNode.Parse("""{ "id": "up", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0 } }"""));
        nodes.Add(JsonNode.Parse("""{ "id": "both", "kind": "and" }"""));
        JsonArray wires = graph["wires"]!.AsArray();
        wires.Remove(wires.Single(wire => (string)wire!["to"]! == "first.value"));
        wires.Add(JsonNode.Parse("""{ "from": "high.result", "to": "both.a" }"""));
        wires.Add(JsonNode.Parse("""{ "from": "btc.close", "to": "up.a" }"""));
        wires.Add(JsonNode.Parse("""{ "from": "up.result", "to": "both.b" }"""));
        wires.Add(JsonNode.Parse("""{ "from": "both.result", "to": "first.value" }"""));
        return graph;
    }

    public static JsonObject Ladder()
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ladder.json")))!.AsObject();
    }

    public static async Task SaveStrategy(HttpClient http, string name, JsonObject graph, string kind)
    {
        HttpResponseMessage saved = await http.PostAsJsonAsync("/api/strategies", new JsonObject { ["name"] = name, ["kind"] = kind, ["graph"] = graph, ["layout"] = new JsonObject() }, Json);
        Assert.That(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
    }

    public static async Task ReplaceStrategy(HttpClient http, string name, JsonObject graph)
    {
        HttpResponseMessage saved = await http.PutAsJsonAsync($"/api/strategies/{name}", new JsonObject { ["graph"] = graph, ["layout"] = new JsonObject() }, Json);
        Assert.That(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> Post(HttpClient http, JsonObject request)
    {
        return http.PostAsJsonAsync("/api/backtests", request, Json);
    }

    public static async Task<BacktestResponse> Queued(HttpClient http, JsonObject request)
    {
        HttpResponseMessage created = await Post(http, request);
        Assert.That(created.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<BacktestResponse>(Json))!;
    }

    public static async Task<BacktestResponse> Read(HttpClient http, string id)
    {
        return (await http.GetFromJsonAsync<BacktestResponse>($"/api/backtests/{id}", Json))!;
    }

    /// <summary>
    ///     Waits until the backtest has ended and gives it.
    /// </summary>
    public static async Task<BacktestResponse> Ended(HttpClient http, string id)
    {
        await Poll.Until($"the end of backtest {id}", async () => (await Read(http, id)).State is "done" or "failed" or "cancelled");
        return await Read(http, id);
    }

    public static async Task<T?> Scalar<T>(string sql, object? parameters = null)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }
}
