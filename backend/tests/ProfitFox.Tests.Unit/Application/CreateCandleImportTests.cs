using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Commands.CreateCandleImport;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

public class CreateCandleImportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly DateTimeOffset To = DateTimeOffset.Parse("2024-02-01T00:00:00Z");

    private IMarketHistoryClient _marketHistory = null!;
    private ICandleImportJobRepository _jobs = null!;
    private ICandleImportQueue _queue = null!;
    private CreateCandleImportCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _marketHistory = Substitute.For<IMarketHistoryClient>();
        _marketHistory.InstrumentExistsAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), Arg.Any<CancellationToken>()).Returns(true);
        _jobs = Substitute.For<ICandleImportJobRepository>();
        _queue = Substitute.For<ICandleImportQueue>();
        _handler = new CreateCandleImportCommandHandler(_marketHistory, _jobs, _queue, new FakeTimeProvider(Now));
    }

    // Scenario "Корректный запрос".
    [Test]
    public async Task Valid_request_creates_a_pending_job_and_queues_it()
    {
        Guid id = await _handler.Handle(new CreateCandleImportCommand("linear", "btcusdt", "60", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z"), CancellationToken.None);

        var expected = new CandleImportJob(
            id, new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, From, To, CandleImportStatus.Pending, ImportedCandles: 0, Error: null, Now, StartedAt: null, FinishedAt: null);
        await _jobs.Received(1).AddAsync(expected, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(id, Arg.Any<CancellationToken>());
    }

    // Scenario "Конец периода не указан".
    [Test]
    public async Task Missing_end_of_period_means_now()
    {
        await _handler.Handle(new CreateCandleImportCommand("linear", "BTCUSDT", "60", "2024-01-01T00:00:00Z", To: null), CancellationToken.None);

        AddedJob().To.ShouldBe(Now);
    }

    [Test]
    public async Task Period_is_converted_to_utc()
    {
        await _handler.Handle(
            new CreateCandleImportCommand("linear", "BTCUSDT", "60", "2024-01-01T03:00:00+03:00", "2024-02-01T03:00:00+03:00"),
            CancellationToken.None);

        CandleImportJob job = AddedJob();
        (job.From.Offset, job.To.Offset).ShouldBe((TimeSpan.Zero, TimeSpan.Zero));
        (job.From, job.To).ShouldBe((From, To));
    }

    // Scenarios "Неподдерживаемая категория", "Неподдерживаемый интервал", "Пустой период", "Время без смещения" and a period in the future.
    [TestCase("inverse", "60", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z", "category")]
    [TestCase("linear", "2", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z", "interval")]
    [TestCase("linear", "60", "2024-01-01T00:00:00Z", "2024-01-01T00:00:00Z", "from")]
    [TestCase("linear", "60", "2024-03-10T13:00:00Z", "2024-03-10T14:00:00Z", "from")]
    [TestCase("linear", "60", "2024-01-01T00:00:00", "2024-02-01T00:00:00Z", "from")]
    [TestCase("linear", "60", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00", "to")]
    public async Task Invalid_parameter_is_rejected_without_creating_a_job(string category, string interval, string from, string to, string invalidField)
    {
        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() => _handler.Handle(
            new CreateCandleImportCommand(category, "BTCUSDT", interval, from, to), CancellationToken.None));

        error.Errors.Keys.ShouldBe([invalidField]);
        await _jobs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
        await _marketHistory.DidNotReceiveWithAnyArgs().InstrumentExistsAsync(default!, default);
    }

    // Scenario "Неизвестный символ".
    [Test]
    public async Task Unknown_symbol_is_rejected_without_creating_a_job()
    {
        ValidationFailedException error = await Should.ThrowAsync<ValidationFailedException>(() =>
            _handler.Handle(new CreateCandleImportCommand("spot", "NOSUCHUSDT", "60", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z"), CancellationToken.None));

        error.Errors.Keys.ShouldBe(["symbol"]);
        await _jobs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
    }

    // Scenario "Bybit недоступен при проверке".
    [Test]
    public async Task Bybit_unavailable_rejects_the_request_without_creating_a_job()
    {
        _marketHistory.InstrumentExistsAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeException("Bybit is unavailable"));

        await Should.ThrowAsync<ExchangeException>(() =>
            _handler.Handle(new CreateCandleImportCommand("linear", "BTCUSDT", "60", "2024-01-01T00:00:00Z", "2024-02-01T00:00:00Z"), CancellationToken.None));
        await _jobs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private CandleImportJob AddedJob() =>
        (CandleImportJob)_jobs.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ICandleImportJobRepository.AddAsync)).GetArguments()[0]!;
}
