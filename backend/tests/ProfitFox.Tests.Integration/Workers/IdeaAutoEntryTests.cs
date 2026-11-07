using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>
///     The requests of the autos with money in trading Workers on the fake exchange: the upper auto enters, the lower finds the pair taken, Workers work one
///     request at a time, and two requests do not go over a budget together.
/// </summary>
public class IdeaAutoEntryTests
{
    private LiveTradingScenario _scenario = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scenario = await LiveTradingScenario.CreateAsync();
        _database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        _scenario.SetPrice("105");

        // The risk of two trades of 100 USDT is above the default total risk of 5.
        await new IdeaTradingSettingsRepository(_database).SaveAsync(IdeaTradingSettings.Default with { MaxTotalRisk = 100m, MaxOpenTrades = 10 }, CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _database.DisposeAsync();
        await _scenario.DisposeAsync();
    }

    // Scenario "Верхний выигрывает", with two Workers: the requests go one at a time, so the upper auto takes the pair and the lower is refused by name.
    [Test]
    public async Task Upper_auto_enters_with_a_stop_and_a_target_and_the_lower_finds_the_pair_taken()
    {
        Guid idea = await _scenario.SaveIdeaAsync(105m, 100m, 115m);
        IdeaAuto upper = await SaveAutoAsync("Импульс лонг", 100m);
        IdeaAuto lower = await SaveAutoAsync("Падения шорт", 100m);
        await SaveEntryAsync(idea, lower, rank: 1);
        await SaveEntryAsync(idea, upper, rank: 0);
        _scenario.StartWorker(inspectKeys: true);
        _scenario.StartWorker(inspectKeys: true);

        await _scenario.WaitAsync(async () => await CountAsync("SELECT count(*) FROM idea_auto_entries") == 0, "the requests worked");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry of the upper auto");

        IdeaTrade trade = (await TradesAsync()).Single();
        (trade.AutoId, trade.AccountId, trade.IdeaId, trade.Amount).ShouldBe((upper.Id, _scenario.Account, idea, trade.Quantity * trade.EntryPrice));
        _scenario.Exchange.Orders.Single().Protection.ShouldBe(new ProfitFox.Domain.ValueObjects.IdeaTrades.Protection(115m, 100m));
        (string reasons, string detail) = await RefusalAsync(lower);
        reasons.ShouldBe("pair_taken");
        detail.ShouldContain("Импульс лонг");
        detail.ShouldContain("\"Above\": true");
        (await CountAsync("SELECT count(*) FROM idea_auto_refusals")).ShouldBe(1);
    }

    // Scenario "Сначала верхний не смог": the upper auto is short of money, so the lower one enters.
    [Test]
    public async Task Lower_auto_enters_when_the_upper_could_not()
    {
        Guid idea = await _scenario.SaveIdeaAsync(105m, 100m, 115m);
        IdeaAuto upper = await SaveAutoAsync("Импульс лонг", 100m);
        IdeaAuto lower = await SaveAutoAsync("Падения шорт", 40m);
        await SaveEntryAsync(idea, upper, rank: 0);
        await SaveEntryAsync(idea, lower, rank: 1);
        _scenario.Exchange.Free = 50m;
        _scenario.StartWorker(inspectKeys: true);

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry of the lower auto");

        (await TradesAsync()).Single().AutoId.ShouldBe(lower.Id);
        (string reasons, string detail) = await RefusalAsync(upper);
        reasons.ShouldBe("not_enough_money");
        detail.ShouldContain("\"Actual\": 50");
        detail.ShouldContain("\"Limit\": 100");
    }

    // Scenario "Две заявки с деньгами": the budget of 150 holds one trade of 100 (0.952380 at 105 is 99.9999), so of the two requests of one minute on two pairs,
    // with two Workers, one enters and the other is refused by the budget with what the first took.
    [Test]
    public async Task Two_requests_of_one_auto_do_not_go_over_its_budget_together()
    {
        Instrument eth = AddPair("ETHUSDT");
        Guid btcIdea = await _scenario.SaveIdeaAsync(105m, 100m, 115m);
        Guid ethIdea = await _scenario.SaveIdeaAsync(105m, 100m, 115m, instrument: eth);
        IdeaAuto auto = await SaveAutoAsync("Импульс лонг", 100m, budget: 150m);
        await SaveEntryAsync(btcIdea, auto, rank: 0);
        await SaveEntryAsync(ethIdea, auto, rank: 0);
        _scenario.StartWorker(inspectKeys: true);
        _scenario.StartWorker(inspectKeys: true);

        await _scenario.WaitAsync(async () => await CountAsync("SELECT count(*) FROM idea_auto_entries") == 0, "the requests worked");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry of the first request");

        IdeaTrade trade = (await TradesAsync()).Single();
        trade.AutoId.ShouldBe(auto.Id);
        (string reasons, string detail) = await RefusalAsync(auto);
        reasons.ShouldBe("budget_taken");
        trade.Amount.ShouldBe(99.9999m);
        detail.ShouldContain("\"Actual\": 99.9999");
        detail.ShouldContain("\"Limit\": 150");
    }

    // Scenario "Бюджет автоматов занят": the budget of all the autos of 150 holds one trade of 100, so the second auto is refused by it.
    [Test]
    public async Task Requests_of_two_autos_do_not_go_over_the_budget_of_all_the_autos_together()
    {
        await new IdeaTradingSettingsRepository(_database).SaveAsync(IdeaTradingSettings.Default with { MaxTotalRisk = 100m, MaxOpenTrades = 10, AutosBudget = 150m }, CancellationToken.None);
        Instrument eth = AddPair("ETHUSDT");
        Guid btcIdea = await _scenario.SaveIdeaAsync(105m, 100m, 115m);
        Guid ethIdea = await _scenario.SaveIdeaAsync(105m, 100m, 115m, instrument: eth);
        IdeaAuto upper = await SaveAutoAsync("Импульс лонг", 100m);
        IdeaAuto lower = await SaveAutoAsync("Пробои 5м", 100m);
        await SaveEntryAsync(btcIdea, upper, rank: 0);
        await SaveEntryAsync(ethIdea, lower, rank: 1);
        _scenario.StartWorker(inspectKeys: true);

        await _scenario.WaitAsync(async () => await CountAsync("SELECT count(*) FROM idea_auto_entries") == 0, "the requests worked");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry of the upper auto");

        IdeaTrade trade = (await TradesAsync()).Single();
        trade.AutoId.ShouldBe(upper.Id);
        (string reasons, string detail) = await RefusalAsync(lower);
        reasons.ShouldBe("autos_budget_taken");
        trade.Amount.ShouldBe(99.9999m);
        detail.ShouldContain("\"Actual\": 99.9999");
        detail.ShouldContain("\"Limit\": 150");
    }

    // A second linear pair on the fake Bybit, priced like the first.
    private Instrument AddPair(string symbol)
    {
        _scenario.Factory.Bybit.Instruments.Add(("linear", symbol));
        _scenario.Factory.Bybit.Coins[("linear", symbol)] = (symbol[..^4], "USDT");
        _scenario.Factory.Bybit.Rules[("linear", symbol)] = ("0.01", "0.000001", "0.000001", "1");
        var instrument = new Instrument(MarketCategory.Linear, symbol);
        _scenario.SetPrice("105", instrument);

        return instrument;
    }

    private async Task<IdeaAuto> SaveAutoAsync(string name, decimal amount, decimal? budget = null)
    {
        var repository = new IdeaAutoRepository(_database, new PostgresTransactionAccessor());
        IdeaAuto auto = await repository.CreateAsync(IdeaAutoGenerator.Generate(_scenario.Account, name, amount: amount) with { Budget = budget }, DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.SetModeAsync(auto.Id, IdeaAutoMode.Live, CancellationToken.None);

        return auto with { Mode = IdeaAutoMode.Live };
    }

    // The request as the scanner writes it. The clock of the test runs a second in 25 ms while a Worker starts, so it is written ahead of the clock to be on time when the Worker takes it.
    private async Task SaveEntryAsync(Guid idea, IdeaAuto auto, int rank)
    {
        DateTimeOffset now = _scenario.Factory.Time.GetUtcNow();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO idea_auto_entries (id, idea_id, auto_id, account_id, amount, min_risk_reward, rank, fired, minute, created_at) VALUES (gen_random_uuid(), @idea, @auto, @account, @amount, 1.2, @rank, 0, @minute, @created)",
            new { idea, auto = auto.Id, account = _scenario.Account, amount = auto.Amount, rank, minute = now.AddMinutes(-1), created = now.AddMinutes(15) });
    }

    private async Task<IReadOnlyList<IdeaTrade>> TradesAsync()
    {
        using IServiceScope scope = _scenario.Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ProfitFox.Domain.Repositories.IIdeaTradeRepository>().GetAllAsync(CancellationToken.None);
    }

    private async Task<(string Reasons, string Detail)> RefusalAsync(IdeaAuto auto)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();
        dynamic row = await connection.QuerySingleAsync("SELECT array_to_string(reasons, ',') AS reasons, detail::text AS detail FROM idea_auto_refusals WHERE auto_id = @id", new { id = auto.Id });

        return (row.reasons, row.detail);
    }

    private async Task<int> CountAsync(string sql)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<int>(sql);
    }
}