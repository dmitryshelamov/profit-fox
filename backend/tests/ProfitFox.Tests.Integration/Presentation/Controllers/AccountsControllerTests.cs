using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.Exceptions;
using ProfitFox.Presentation;
using ProfitFox.Presentation.Contracts.Accounts;
using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Fakes.Exchange;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change add-exchange-accounts, task 5.3 (design.md, decisions 6 and 8): <c>/api/accounts</c> over the test database
///     with Bybit in memory — the answers 200, 201, 204, 404, 409, 422 at a field, 502 and 503, and spec exchange-accounts
///     «Секрета в ответе нет» in the bodies and in the OpenAPI document.
/// </summary>
[NonParallelizable]
public sealed class AccountsControllerTests
{
    private const string Secret = "s3cr3t-value";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private FakeKeyInspector _bybit = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _bybit = new FakeKeyInspector();
        _api = new BenchTestApplication(_market, exchange: _bybit);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
    }

    // Spec «Два аккаунта», «Секрета в ответе нет».
    [Test]
    public async Task Added_accounts_are_listed_and_no_answer_has_the_secret()
    {
        _bybit.Key("key-main", FakeKeyInspector.Trading(104857321)).Key("key-ideas", FakeKeyInspector.Trading(104850011));

        HttpResponseMessage main = await Add("Основной", "key-main");
        HttpResponseMessage ideas = await Add("Суб-счёт идей", "key-ideas");
        HttpResponseMessage list = await _api.Http.GetAsync("/api/accounts");

        Assert.That(main.StatusCode, Is.EqualTo(HttpStatusCode.Created), await main.Content.ReadAsStringAsync());
        Assert.That(ideas.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        AccountResponse[] accounts = (await list.Content.ReadFromJsonAsync<AccountResponse[]>(Json))!;
        Assert.That(accounts.Select(account => (account.Name, account.Uid, account.ApiKey)), Is.EqualTo(new[] { ("Основной", 104857321L, "key-main"), ("Суб-счёт идей", 104850011L, "key-ideas") }));
        Assert.That(accounts[0], Is.EqualTo(await main.Content.ReadFromJsonAsync<AccountResponse>(Json)));
        foreach (HttpResponseMessage answer in new[] { main, ideas, list })
        {
            string body = await answer.Content.ReadAsStringAsync();
            Assert.That(body, Does.Not.Contain(Secret).And.Not.Contain("secret").IgnoreCase);
        }
    }

    // Spec «Пустой секрет», «Имя занято»: 422 at the field.
    [Test]
    public async Task A_refused_field_answers_422_at_its_field()
    {
        HttpResponseMessage empty = await _api.Http.PostAsJsonAsync("/api/accounts", new { name = "Основной", apiKey = "key-main", secret = " " }, Json);
        await Add("Основной", "key-main");
        _bybit.Key("key-2", FakeKeyInspector.Trading(2));
        HttpResponseMessage taken = await Add("основной", "key-2");

        Assert.That(await Errors(empty), Is.EqualTo(new[] { ("Required", "secret") }));
        Assert.That(await Errors(taken), Is.EqualTo(new[] { ("TakenName", "name") }));
        Assert.That(_bybit.Calls.Where(call => call.Call == "inspect").Select(call => call.ApiKey), Is.EqualTo(new[] { "key-main", "key-2" }), "the empty secret did not reach Bybit");
    }

    // Spec «Bybit не знает ключ», «Ключ с правом вывода», «Bybit недоступен»; design.md, decision 6.
    [Test]
    public async Task The_refusals_of_Bybit_answer_by_the_table_of_the_design()
    {
        _bybit.Key("unknown", new ExchangeKeyRejectedException("API key is invalid."))
            .Key("withdraw", FakeKeyInspector.Trading() with { Wallet = ["Withdraw"] })
            .Key("down", new ExchangeUnavailableException("Bybit is unavailable: timeout"))
            .Key("clock", new ExchangeException("invalid request, please check your server timestamp or recv_window param"));

        HttpResponseMessage unknown = await Add("A", "unknown");
        HttpResponseMessage withdraw = await Add("B", "withdraw");
        HttpResponseMessage down = await Add("C", "down");
        HttpResponseMessage clock = await Add("D", "clock");

        ErrorResponse rejected = (await unknown.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(rejected.Errors.Single(), Is.EqualTo(new RequestError("KeyRejected", "API key is invalid.") { Field = "apiKey" }));
        Assert.That(await Errors(withdraw), Is.EqualTo(new[] { ("WithdrawPermission", "apiKey") }));
        Assert.That(down.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await Detail(down), Does.Contain("Bybit не отвечает"));
        Assert.That(clock.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(await Detail(clock), Is.EqualTo("invalid request, please check your server timestamp or recv_window param"));
        Assert.That((await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!, Is.Empty, "nothing was saved");
    }

    // Spec «Новое имя», «Имя другого аккаунта», «Аккаунт удалён».
    [Test]
    public async Task An_account_is_renamed_and_deleted_and_one_that_is_not_there_is_404()
    {
        _bybit.Key("key-old", FakeKeyInspector.Trading(104850011)).Key("key-main", FakeKeyInspector.Trading(104857321));
        AccountResponse old = (await (await Add("Старый", "key-old")).Content.ReadFromJsonAsync<AccountResponse>(Json))!;
        await Add("Основной", "key-main");

        HttpResponseMessage renamed = await _api.Http.PutAsJsonAsync($"/api/accounts/{old.Id}", new { name = "Запасной" }, Json);
        HttpResponseMessage taken = await _api.Http.PutAsJsonAsync($"/api/accounts/{old.Id}", new { name = "ОСНОВНОЙ" }, Json);
        HttpResponseMessage missingRename = await _api.Http.PutAsJsonAsync($"/api/accounts/{Guid.NewGuid()}", new { name = "Запасной 2" }, Json);
        HttpResponseMessage deleted = await _api.Http.DeleteAsync($"/api/accounts/{old.Id}");
        HttpResponseMessage missingDelete = await _api.Http.DeleteAsync($"/api/accounts/{old.Id}");

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await renamed.Content.ReadFromJsonAsync<AccountResponse>(Json), Is.EqualTo(old with { Name = "Запасной" }));
        Assert.That(await Errors(taken), Is.EqualTo(new[] { ("TakenName", "name") }));
        Assert.That(missingRename.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(missingDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!.Select(account => account.Name), Is.EqualTo(new[] { "Основной" }));
    }

    // Spec «Баланс показан», «Ключ больше не действует»; 404, 502 and 503 of the balance.
    [Test]
    public async Task The_balance_is_read_at_Bybit_with_the_secret_and_its_refusals_answer_by_the_table()
    {
        AccountResponse account = (await (await Add("Основной", "key-main")).Content.ReadFromJsonAsync<AccountResponse>(Json))!;
        _bybit.Free("key-main", 1250.40m);

        HttpResponseMessage shown = await _api.Http.GetAsync($"/api/accounts/{account.Id}/balance");
        _bybit.Free("key-main", new ExchangeKeyRejectedException("Your api key has expired."));
        HttpResponseMessage expired = await _api.Http.GetAsync($"/api/accounts/{account.Id}/balance");
        _bybit.Free("key-main", new ExchangeUnavailableException("Bybit is unavailable"));
        HttpResponseMessage down = await _api.Http.GetAsync($"/api/accounts/{account.Id}/balance");
        _bybit.Free("key-main", new ExchangeException("Params error."));
        HttpResponseMessage other = await _api.Http.GetAsync($"/api/accounts/{account.Id}/balance");
        HttpResponseMessage missing = await _api.Http.GetAsync($"/api/accounts/{Guid.NewGuid()}/balance");

        Assert.That(shown.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await shown.Content.ReadFromJsonAsync<AccountBalanceResponse>(Json), Is.EqualTo(new AccountBalanceResponse(1250.40m, null)));
        Assert.That(_bybit.Calls.Where(call => call.Call == "free").Select(call => call.Secret).Distinct(), Is.EqualTo(new[] { Secret }));
        Assert.That((await expired.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Single(), Is.EqualTo(new RequestError("KeyRejected", "Your api key has expired.") { Field = "apiKey" }));
        Assert.That(down.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!, Has.Length.EqualTo(1), "the account stays");
    }

    // Change add-exchange-accounts, task 5.5: spec «Режим запомнен при добавлении», «Режим обновлён по „Показать“», «Список не
    // ходит к Bybit», «Отказ при чтении режима».
    [Test]
    public async Task The_margin_mode_is_kept_at_the_addition_renewed_by_the_balance_and_listed_without_Bybit()
    {
        _bybit.Mode("key-main", "ISOLATED_MARGIN");
        AccountResponse added = (await (await Add("Основной", "key-main")).Content.ReadFromJsonAsync<AccountResponse>(Json))!;
        _bybit.Free("key-main", 10m).Mode("key-main", "REGULAR_MARGIN");
        int before = _bybit.Calls.Count;
        AccountResponse[] listed = (await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!;
        int listCalls = _bybit.Calls.Count - before;

        AccountBalanceResponse balance = (await _api.Http.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{added.Id}/balance", Json))!;
        AccountResponse[] after = (await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!;

        Assert.That(added.MarginMode, Is.EqualTo("ISOLATED_MARGIN"));
        Assert.That(listed.Single().MarginMode, Is.EqualTo("ISOLATED_MARGIN"));
        Assert.That(listCalls, Is.Zero, "the list does not call Bybit");
        Assert.That(balance, Is.EqualTo(new AccountBalanceResponse(10m, "REGULAR_MARGIN")));
        Assert.That(after.Single(), Is.EqualTo(added with { MarginMode = "REGULAR_MARGIN" }));
    }

    [Test]
    public async Task A_refused_margin_mode_answers_by_the_table_and_saves_nothing()
    {
        _bybit.Mode("down", new ExchangeUnavailableException("Bybit is unavailable")).Mode("denied", new ExchangeKeyRejectedException("Permission denied."));

        HttpResponseMessage down = await Add("A", "down");
        HttpResponseMessage denied = await Add("B", "denied");

        Assert.That(down.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await Errors(denied), Is.EqualTo(new[] { ("KeyRejected", (string?)"apiKey") }));
        Assert.That((await _api.Http.GetFromJsonAsync<AccountResponse[]>("/api/accounts", Json))!, Is.Empty);
    }

    // Spec «Сменили мастер-ключ»: the API started again with another master key over the same database answers 409.
    [Test]
    public async Task A_secret_of_another_master_key_answers_409()
    {
        AccountResponse account = (await (await Add("Основной", "key-main")).Content.ReadFromJsonAsync<AccountResponse>(Json))!;
        await using var changed = new BenchTestApplication(_market, masterKey: Convert.ToBase64String(new byte[32]), exchange: _bybit);

        HttpResponseMessage balance = await changed.Http.GetAsync($"/api/accounts/{account.Id}/balance");

        Assert.That(balance.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Detail(balance), Does.Contain("добавьте его заново"));
        Assert.That(_bybit.Calls.Where(call => call.Call == "free"), Is.Empty, "Bybit is not asked without the secret");
    }

    // Spec «Секрета в ответе нет»: no answer of /api/accounts has a field of the secret; only the request of an addition has one.
    [Test]
    public async Task The_OpenAPI_document_has_no_field_of_the_secret_in_an_answer()
    {
        await using WebApplicationFactory<Program> development = _api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bench"] = BenchDatabase.ConnectionString,
                ["ExchangeAccounts:MasterKey"] = BenchTestApplication.TestMasterKey
            }));
        });
        using HttpClient http = development.CreateClient();

        JsonNode document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"))!;

        JsonObject schemas = document["components"]!["schemas"]!.AsObject();
        Assert.That(Properties(schemas["AccountResponse"]!), Is.EqualTo(new[] { "id", "name", "uid", "apiKey", "marginMode", "createdAt" }));
        Assert.That(Properties(schemas["AddAccountRequest"]!), Is.EqualTo(new[] { "name", "apiKey", "secret" }));
        string[] withSecret = [.. schemas.Where(schema => Properties(schema.Value!).Any(name => name.Contains("secret", StringComparison.OrdinalIgnoreCase))).Select(schema => schema.Key)];
        Assert.That(withSecret, Is.EqualTo(new[] { "AddAccountRequest" }));
        foreach (string path in new[] { "/api/accounts", "/api/accounts/{id}", "/api/accounts/{id}/balance" })
        {
            Assert.That(document["paths"]![path], Is.Not.Null, path);
        }
    }

    private Task<HttpResponseMessage> Add(string name, string apiKey)
    {
        return _api.Http.PostAsJsonAsync("/api/accounts", new AddAccountRequest(name, apiKey, Secret), Json);
    }

    private static async Task<(string Code, string? Field)[]> Errors(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        ErrorResponse errors = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json))!;
        return [.. errors.Errors.Select(error => (error.Code, error.Field))];
    }

    private static async Task<string> Detail(HttpResponseMessage response)
    {
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>();
    }

    private static string[] Properties(JsonNode schema)
    {
        return schema["properties"] is JsonObject properties ? [.. properties.Select(property => property.Key)] : [];
    }
}
