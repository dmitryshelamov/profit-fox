using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class CandleImportsApiTests
{
    private const string Imports = "/api/market-data/candle-imports";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Корректный запрос". Imports do not run here: otherwise the job may already be Running when the response is built.
    [Test]
    public async Task Valid_request_is_accepted_as_a_pending_job()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Imports,
            new
            {
                category = "linear",
                symbol = "BTCUSDT",
                interval = "60",
                from = "2024-01-01T00:00:00Z",
                to = "2024-02-01T00:00:00Z"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonElement job = await response.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("status").GetString().ShouldBe("Pending");
        job.GetProperty("category").GetString().ShouldBe("linear");
        job.GetProperty("interval").GetString().ShouldBe("60");
        response.Headers.Location!.ToString().ShouldEndWith($"{Imports}/{job.GetProperty("id").GetGuid()}");
    }

    [Test]
    public async Task Created_job_is_found_by_its_id()
    {
        JsonElement created = await (await _client.PostAsJsonAsync(
            Imports,
            new
            {
                category = "linear",
                symbol = "btcusdt",
                interval = "60",
                from = "2024-01-01T00:00:00Z",
                to = "2024-02-01T00:00:00Z"
            })).Content.ReadFromJsonAsync<JsonElement>();

        HttpResponseMessage response = await _client.GetAsync($"{Imports}/{created.GetProperty("id").GetGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement job = await response.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("symbol").GetString().ShouldBe("BTCUSDT");
        job.GetProperty("from").GetDateTimeOffset().ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
    }

    // Scenarios "Неподдерживаемая категория", "Неподдерживаемый интервал", "Пустой период", "Неизвестный символ",
    // "Время без смещения" and a missing start.
    [TestCase("inverse", "BTCUSDT", "60", "2024-01-01T00:00:00Z", "category")]
    [TestCase("linear", "BTCUSDT", "2", "2024-01-01T00:00:00Z", "interval")]
    [TestCase("linear", "BTCUSDT", "60", "2024-02-01T00:00:00Z", "from")]
    [TestCase("spot", "NOSUCHUSDT", "60", "2024-01-01T00:00:00Z", "symbol")]
    [TestCase("linear", "BTCUSDT", "60", null, "from")]
    [TestCase("linear", "BTCUSDT", "60", "2024-01-01T00:00:00", "from")]
    public async Task Invalid_request_is_rejected_with_the_invalid_parameter(string category, string symbol, string interval, string? from, string invalidField)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Imports, new { category, symbol, interval, from, to = "2024-02-01T00:00:00Z" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(new[] { invalidField });
    }

    // Scenario "Пропущен обязательный параметр": Bybit is down, so only validation can answer 400.
    [TestCase("")]
    [TestCase(" ")]
    public async Task Empty_symbol_is_rejected_before_asking_bybit(string symbol)
    {
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage response = await _client.PostAsJsonAsync(Imports, new { category = "linear", symbol, interval = "60", from = "2024-01-01T00:00:00Z" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(new[] { "symbol" });
    }

    // Scenario "Пропущен обязательный параметр": the error names the missing parameter, not the JSON root.
    [Test]
    public async Task Missing_parameter_is_reported_by_its_name()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Imports, new { category = "linear", interval = "60", from = "2024-01-01T00:00:00Z" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(new[] { "symbol" });
    }

    // Scenario "Bybit недоступен при проверке".
    [Test]
    public async Task Bybit_unavailable_is_service_unavailable()
    {
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            Imports,
            new
            {
                category = "linear",
                symbol = "BTCUSDT",
                interval = "60",
                from = "2024-01-01T00:00:00Z",
                to = "2024-02-01T00:00:00Z"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    // Scenario "Неизвестная задача".
    [Test]
    public async Task Unknown_job_is_not_found()
    {
        HttpResponseMessage response = await _client.GetAsync($"{Imports}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Повтор упавшей задачи". Imports do not run here, so the retried job stays Pending.
    [Test]
    public async Task Failed_job_retry_is_accepted_as_a_pending_job()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        using HttpClient client = factory.CreateClient();
        CandleImportJob failed = await AddJobAsync(
            factory,
            CandleImportJobGenerator.Generate(CandleImportStatus.Failed) with { Error = "params error", FinishedAt = factory.Time.GetUtcNow() });

        HttpResponseMessage response = await client.PostAsync($"{Imports}/{failed.Id}/retry", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().ShouldEndWith($"{Imports}/{failed.Id}");
        JsonElement job = await response.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("id").GetGuid().ShouldBe(failed.Id);
        job.GetProperty("status").GetString().ShouldBe("Pending");
        job.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Scenario "Повтор незавершённой или успешной задачи".
    [Test]
    public async Task Retry_of_a_job_that_has_not_failed_is_a_conflict()
    {
        CandleImportJob succeeded = await AddJobAsync(_factory, CandleImportJobGenerator.Generate(CandleImportStatus.Succeeded));

        HttpResponseMessage response = await _client.PostAsync($"{Imports}/{succeeded.Id}/retry", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    // Scenario "Повтор неизвестной задачи".
    [Test]
    public async Task Retry_of_an_unknown_job_is_not_found()
    {
        HttpResponseMessage response = await _client.PostAsync($"{Imports}/{Guid.NewGuid()}/retry", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<CandleImportJob> AddJobAsync(ProfitFoxFactory factory, CandleImportJob job)
    {
        await using var dataSource = NpgsqlDataSource.Create(factory.ConnectionString);
        await new CandleImportJobRepository(dataSource).AddAsync(job, CancellationToken.None);

        return job;
    }
}