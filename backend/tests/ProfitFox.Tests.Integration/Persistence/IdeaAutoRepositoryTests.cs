using System.Text;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.IdeaAutos;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class IdeaAutoRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-07T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaAutoRepository _repository = null!;
    private Guid _account;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _repository = new IdeaAutoRepository(_dataSource, new PostgresTransactionAccessor());
        var account = new ExchangeAccount(Guid.NewGuid(), "Main", "key", 100, new HashSet<MarketCategory> { MarketCategory.Linear }, Now);
        await new ExchangeAccountRepository(_dataSource).AddAsync(account, new EncryptedSecret([1], Encoding.UTF8.GetBytes("cipher"), [2]), CancellationToken.None);
        _account = account.Id;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Scenario "Новый автомат": every condition survives the round trip and the new auto stands after the two that were.
    [Test]
    public async Task Auto_is_stored_with_its_conditions_at_the_end_of_the_list()
    {
        await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "А"), Now, CancellationToken.None);
        await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Б"), Now, CancellationToken.None);
        var conditions = new IdeaAutoConditions(
            [IdeaSetup.Breakout, IdeaSetup.Squeeze],
            [IdeaType.Levels],
            [Interval.FiveMinutes, Interval.FourHours],
            IdeaSide.Short,
            MarketCategory.Spot,
            IdeaBadge.Flat,
            1.35m,
            100);
        IdeaAuto auto = IdeaAutoGenerator.Generate(_account, "Импульс лонг", conditions: conditions);

        IdeaAuto created = await _repository.CreateAsync(auto, Now, CancellationToken.None);

        created.Position.ShouldBe(2);
        Shape((await _repository.GetAsync(auto.Id, CancellationToken.None))!).ShouldBe(Shape(auto with { Position = 2 }));
        (await _repository.GetAllAsync(CancellationToken.None)).Select(item => item.Name).ShouldBe(["А", "Б", "Импульс лонг"]);
        (await _repository.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Save_writes_the_name_the_account_the_amount_and_the_conditions_but_not_the_mode_or_the_place()
    {
        IdeaAuto created = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "А", mode: IdeaAutoMode.Paper), Now, CancellationToken.None);
        IdeaAutoConditions conditions = created.Conditions with { Side = IdeaSide.Long, MinCases = 50, Types = [IdeaType.Pullback] };

        (await _repository.SaveAsync(created with { Name = "Б", Amount = 150m, Conditions = conditions, Mode = IdeaAutoMode.Live, Position = 9 }, CancellationToken.None)).ShouldBeTrue();

        Shape((await _repository.GetAsync(created.Id, CancellationToken.None))!).ShouldBe(Shape(created with { Name = "Б", Amount = 150m, Conditions = conditions }));
        (await _repository.SaveAsync(IdeaAutoGenerator.Generate(_account), CancellationToken.None)).ShouldBeFalse();
    }

    // Scenario "Перестановка": «А», «Б», «В» become «В», «А», «Б».
    [Test]
    public async Task Reorder_gives_the_autos_the_places_of_their_ids()
    {
        IdeaAuto first = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "А"), Now, CancellationToken.None);
        IdeaAuto second = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Б"), Now, CancellationToken.None);
        IdeaAuto third = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "В"), Now, CancellationToken.None);

        await _repository.ReorderAsync([third.Id, first.Id, second.Id], CancellationToken.None);

        IReadOnlyList<IdeaAuto> all = await _repository.GetAllAsync(CancellationToken.None);
        all.Select(item => (item.Name, item.Position)).ShouldBe([("В", 0), ("А", 1), ("Б", 2)]);
    }

    [Test]
    public async Task Mode_is_changed_at_once_and_the_enabled_autos_are_those_not_off_and_not_deleted()
    {
        IdeaAuto off = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Выкл"), Now, CancellationToken.None);
        IdeaAuto paper = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Учебный"), Now, CancellationToken.None);
        IdeaAuto live = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Деньги"), Now, CancellationToken.None);
        IdeaAuto deleted = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Удалён"), Now, CancellationToken.None);

        (await _repository.SetModeAsync(paper.Id, IdeaAutoMode.Paper, CancellationToken.None)).ShouldBeTrue();
        await _repository.SetModeAsync(live.Id, IdeaAutoMode.Live, CancellationToken.None);
        await _repository.SetModeAsync(deleted.Id, IdeaAutoMode.Live, CancellationToken.None);
        await _repository.DeleteAsync(deleted.Id, Now, CancellationToken.None);

        (await _repository.GetEnabledAsync(CancellationToken.None)).Select(item => item.Name).ShouldBe(["Учебный", "Деньги"]);
        (await _repository.SetModeAsync(deleted.Id, IdeaAutoMode.Paper, CancellationToken.None)).ShouldBeFalse();
        off.Mode.ShouldBe(IdeaAutoMode.Off);
    }

    // Scenarios "Имя занято" and "Удаление автомата": the name of a deleted auto is free, the auto itself stays for the names of its trades.
    [Test]
    public async Task Name_is_taken_while_the_auto_stands_and_free_after_it_is_deleted()
    {
        IdeaAuto first = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Пробои 5м"), Now, CancellationToken.None);

        await Should.ThrowAsync<PostgresException>(() => _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Пробои 5м"), Now, CancellationToken.None));

        (await _repository.DeleteAsync(first.Id, Now, CancellationToken.None)).ShouldBeTrue();
        (await _repository.DeleteAsync(first.Id, Now, CancellationToken.None)).ShouldBeFalse();
        IdeaAuto again = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Пробои 5м"), Now, CancellationToken.None);

        (await _repository.GetAllAsync(CancellationToken.None)).Select(item => item.Id).ShouldBe([again.Id]);
        IReadOnlyDictionary<Guid, IdeaAuto> every = await _repository.GetEveryAsync(CancellationToken.None);
        every.Keys.ShouldBe([first.Id, again.Id], ignoreOrder: true);
        every[first.Id].DeletedAt.ShouldBe(Now);
    }

    // Review P2: an entry reads the auto under a share lock; a change of the mode waits for the entry to commit.
    [Test]
    public async Task Change_of_the_mode_waits_for_the_entry_that_locked_the_auto()
    {
        var accessor = new PostgresTransactionAccessor();
        var locking = new IdeaAutoRepository(_dataSource, accessor);
        IdeaAuto auto = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "А", mode: IdeaAutoMode.Live), Now, CancellationToken.None);
        await _repository.SetModeAsync(auto.Id, IdeaAutoMode.Live, CancellationToken.None);
        var release = new TaskCompletionSource();
        Task<IdeaAuto?> locked = new PostgresTransactionExecutor(_dataSource, accessor).ExecuteAsync(
            async token =>
            {
                IdeaAuto? read = await locking.LockAsync(auto.Id, token);
                await release.Task;

                return read;
            },
            CancellationToken.None);
        await Task.Delay(200);

        Task<bool> change = _repository.SetModeAsync(auto.Id, IdeaAutoMode.Off, CancellationToken.None);
        await Task.Delay(300);

        change.IsCompleted.ShouldBeFalse();
        release.SetResult();
        (await locked)!.Mode.ShouldBe(IdeaAutoMode.Live);
        (await change).ShouldBeTrue();
        (await _repository.GetAsync(auto.Id, CancellationToken.None))!.Mode.ShouldBe(IdeaAutoMode.Off);
        (await new PostgresTransactionExecutor(_dataSource, accessor).ExecuteAsync(token => locking.LockAsync(Guid.NewGuid(), token), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Place_of_a_new_auto_comes_after_the_last_one_that_is_not_deleted()
    {
        IdeaAuto first = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "А"), Now, CancellationToken.None);
        IdeaAuto second = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "Б"), Now, CancellationToken.None);
        await _repository.DeleteAsync(second.Id, Now, CancellationToken.None);

        IdeaAuto third = await _repository.CreateAsync(IdeaAutoGenerator.Generate(_account, "В"), Now, CancellationToken.None);

        (first.Position, third.Position).ShouldBe((0, 1));
    }

    // The collections of the conditions are compared by their items, as the records compare them by reference.
    private static object Shape(IdeaAuto auto)
    {
        IdeaAutoConditions conditions = auto.Conditions;

        return (auto.Id, auto.Name, auto.AccountId, auto.Amount, auto.Mode, auto.Position, auto.DeletedAt, string.Join(',', conditions.Setups), string.Join(',', conditions.Types),
            string.Join(',', conditions.Intervals), conditions.Side, conditions.Category, conditions.Badge, conditions.MinRiskReward, conditions.MinCases);
    }
}