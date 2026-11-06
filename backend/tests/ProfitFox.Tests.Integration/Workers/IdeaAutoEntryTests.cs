using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>The requests of the autos with money in trading Workers on the fake exchange: the upper auto enters, the lower finds the pair taken, and Workers work one request at a time.</summary>
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

    private async Task<IdeaAuto> SaveAutoAsync(string name, decimal amount)
    {
        var repository = new IdeaAutoRepository(_database);
        IdeaAuto auto = await repository.CreateAsync(IdeaAutoGenerator.Generate(_scenario.Account, name, amount: amount), DateTimeOffset.UtcNow, CancellationToken.None);
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