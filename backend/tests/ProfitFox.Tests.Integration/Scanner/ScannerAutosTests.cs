using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Integration.Scanner;

/// <summary>
///     The autos in the scanner process, against the fake Bybit and on Postgres: a paper auto takes the impulse of the seeded minutes of
///     <see cref="IdeaFixtures" /> the minute it is born in and writes a paper trade, which the next minutes close at the target, and no private
///     request goes out (the guard of the tests would fail it).
/// </summary>
public class ScannerAutosTests
{
    // The impulse of 15 minutes fires on the minute of 04:55 of 1 March 2024, which closes at 04:56.
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T04:50:20Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Born = DateTimeOffset.Parse("2024-03-01T04:55:00Z", CultureInfo.InvariantCulture);
    private static readonly IReadOnlyDictionary<string, string?> Settings = new Dictionary<string, string?> { ["Scanner:HistoryDays"] = "1", ["Scanner:Symbols:0"] = "SOLUSDT" };

    private string _connectionString = null!;
    private NpgsqlDataSource _database = null!;
    private FakeTimeProvider _time = null!;
    private readonly List<IAsyncDisposable> _disposables = [];

    [SetUp]
    public async Task SetUp()
    {
        _connectionString = IntegrationEnvironment.ConnectionString;
        _database = NpgsqlDataSource.Create(_connectionString);
        _time = new FakeTimeProvider(Start);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IAsyncDisposable disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        _disposables.Clear();
        await _database.DisposeAsync();
    }

    // Requirements "Вход учебной сделки" and "Выход учебной сделки" in the scanner: the idea is born, the paper trade is written with it, the next minute closes it.
    [Test]
    public async Task Paper_auto_writes_a_paper_trade_of_the_impulse_and_the_minutes_after_close_it_at_the_target()
    {
        IdeaAuto auto = await CreatePaperAutoAsync();
        (FakeBybitHandler bybit, FakeBybitSocketServer sockets) = await BybitAsync();
        await StartScannerAsync(bybit, sockets);
        await WaitAsync(() => Task.FromResult(sockets.Subscribed("kline.1.SOLUSDT")));

        // Each minute is pushed as the clock passes its end: pushed at once, the warm-up of the pair would take them in without the ideas looking at them.
        IReadOnlyList<IdeaCandle> impulse = IdeaFixtures.Setup("impulse");
        IdeaCandle birth = impulse.Single(minute => minute.OpenTime == Born);

        foreach (IdeaCandle minute in impulse.Where(minute => minute.OpenTime >= Floor(Start) && minute.OpenTime <= Born))
        {
            await AdvanceToAsync(minute.OpenTime.AddMinutes(1));
            await sockets.PushAsync("kline.1.SOLUSDT", [Kline(minute)]);
        }

        await WaitAsync(async () => await ScalarAsync<int>("SELECT count(*) FROM idea_paper_trades") == 1);

        (string status, Guid autoId, decimal entry, decimal target, decimal quantity) = await RowAsync();
        (status, autoId, entry).ShouldBe(("open", auto.Id, birth.Close));
        quantity.ShouldBeGreaterThan(0m);
        (await ScalarAsync<int>("SELECT count(*) FROM idea_auto_refusals")).ShouldBe(0);

        // The next minute reaches far above the target and not below the stop.
        await AdvanceToAsync(Born.AddMinutes(2));
        await sockets.PushAsync("kline.1.SOLUSDT", [Kline(new IdeaCandle(Born.AddMinutes(1), birth.Close, target + 10m, birth.Close, birth.Close, 300m))]);
        await WaitAsync(async () => await ScalarAsync<string>("SELECT status FROM idea_paper_trades") == "closed_at_target");

        // The long sells at the target less the slippage of linear in the settings, 0.05 %.
        (await ScalarAsync<decimal>("SELECT take_profit_value FROM idea_paper_trades")).ShouldBe(quantity * target * 0.9995m);
        (await ScalarAsync<DateTime>("SELECT status_at FROM idea_paper_trades")).ShouldBe(Born.AddMinutes(1).UtcDateTime);
    }

    private async Task<IdeaAuto> CreatePaperAutoAsync()
    {
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Start);
        await new ExchangeAccountRepository(_database).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        var conditions = new IdeaAutoConditions([IdeaSetup.Impulse], [IdeaType.BeyondExtreme], [Interval.FifteenMinutes], null, null, null, 0.5m, 0);
        var repository = new IdeaAutoRepository(_database, new PostgresTransactionAccessor());
        IdeaAuto auto = await repository.CreateAsync(IdeaAutoGenerator.Generate(account.Id, "Импульс лонг", amount: 50m, conditions: conditions), Start, CancellationToken.None);
        await repository.SetModeAsync(auto.Id, IdeaAutoMode.Paper, CancellationToken.None);

        return auto;
    }

    private async Task<(string Status, Guid AutoId, decimal Entry, decimal Target, decimal Quantity)> RowAsync()
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();
        dynamic row = await connection.QuerySingleAsync("SELECT status, auto_id, entry_price, target, quantity FROM idea_paper_trades");

        return (row.status, row.auto_id, row.entry_price, row.target, row.quantity);
    }

    private static JsonObject Kline(IdeaCandle minute)
    {
        return FakeBybitSocketServer.Kline(minute.OpenTime, TimeSpan.FromMinutes(1), minute.Close, confirm: true, open: minute.Open, high: minute.High, low: minute.Low, turnover: minute.Volume);
    }

    // SOLUSDT on linear with the minutes of the impulse before the start, and the rules of a lot.
    private async Task<(FakeBybitHandler, FakeBybitSocketServer)> BybitAsync()
    {
        var bybit = new FakeBybitHandler();
        bybit.Instruments.Add(("linear", "SOLUSDT"));
        bybit.Coins[("linear", "SOLUSDT")] = ("SOL", "USDT");
        bybit.Rules[("linear", "SOLUSDT")] = ("0.01", "0.01", "0.01", "5");
        bybit.Tickers[("linear", "SOLUSDT")] = ("106.17", "106.1", "106.2", "1000", "0.0001");

        foreach (IdeaCandle minute in IdeaFixtures.Setup("impulse").Where(minute => minute.OpenTime < Floor(Start)))
        {
            bybit.Klines.Add([minute.OpenTime.ToString("O", CultureInfo.InvariantCulture), Text(minute.Open), Text(minute.High), Text(minute.Low), Text(minute.Close), "1", Text(minute.Volume)]);
        }

        FakeBybitSocketServer sockets = await FakeBybitSocketServer.StartAsync();
        _disposables.Add(sockets);

        return (bybit, sockets);
    }

    private async Task StartScannerAsync(FakeBybitHandler bybit, FakeBybitSocketServer sockets)
    {
        var scanner = new ScannerHost(_connectionString, bybit, sockets, _time, Settings);
        _disposables.Add(scanner);
        await scanner.StartAsync();
    }

    private async Task AdvanceToAsync(DateTimeOffset target)
    {
        while (_time.GetUtcNow() < target)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    private async Task WaitAsync(Func<Task<bool>> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(90);

        while (!await condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();

        return (await connection.ExecuteScalarAsync<T>(sql))!;
    }

    private static DateTimeOffset Floor(DateTimeOffset time)
    {
        return time.AddTicks(-(time.UtcTicks % TimeSpan.TicksPerMinute));
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}