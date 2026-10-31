using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Strategies.Commands.CreateStrategy;
using ProfitFox.Application.Strategies.Commands.DeleteStrategy;
using ProfitFox.Application.Strategies.Commands.UpdateStrategy;
using ProfitFox.Application.Strategies.Queries.GetStrategies;
using ProfitFox.Application.Strategies.Queries.GetStrategy;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Strategies;

public class StrategyHandlersTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly Guid Existing = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private IStrategyLibraryRepository _repository = null!;
    private StrategyLibrary _library = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Now);
        _library = new StrategyLibrary(7, [], []);
        _library.AddStrategy(Existing, "EMA", ApplicationStrategiesFixtures.Ema(), Now.AddDays(-1));
        _repository = Substitute.For<IStrategyLibraryRepository>();
        _repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _library);
        _repository.SaveStrategyAsync(Arg.Any<Strategy>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.DeleteStrategyAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Test]
    public async Task Valid_strategy_is_saved_at_the_loaded_revision()
    {
        Guid id = await new CreateStrategyCommandHandler(_repository, _time).Handle(new CreateStrategyCommand("Моя EMA", StrategyFixtures.Element("ema")), CancellationToken.None);

        await _repository.Received(1).SaveStrategyAsync(
            Arg.Is<Strategy>(strategy => strategy!.Id == id && strategy.Name == "Моя EMA" && strategy.CreatedAt == Now),
            7,
            Arg.Any<CancellationToken>());
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Strategy_without_a_name_is_rejected(string name)
    {
        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() =>
            new CreateStrategyCommandHandler(_repository, _time).Handle(new CreateStrategyCommand(name, StrategyFixtures.Element("ema")), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["name"]);
    }

    [Test]
    public void Strategy_with_graph_errors_is_rejected_with_paths()
    {
        JsonElement definition = JsonDocument.Parse("""{ "formatVersion": 1, "direction": "long", "nodes": [ { "id": "mul", "kind": "multiply" } ] }""").RootElement;

        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() =>
            new CreateStrategyCommandHandler(_repository, _time).Handle(new CreateStrategyCommand("x", definition), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["graph.mul.a", "graph.mul.b"], ignoreOrder: true);
        _repository.ReceivedCalls().ShouldNotContain(call => call.GetMethodInfo().Name == nameof(IStrategyLibraryRepository.SaveStrategyAsync));
    }

    [Test]
    public void Malformed_definition_and_name_are_reported_together()
    {
        JsonElement definition = JsonDocument.Parse("""{ "direction": "long", "nodes": [] }""").RootElement;

        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() =>
            new CreateStrategyCommandHandler(_repository, _time).Handle(new CreateStrategyCommand("", definition), CancellationToken.None));

        exception.Errors.Keys.ShouldBe(["name", "definition.formatVersion"], ignoreOrder: true);
    }

    [Test]
    public void Save_on_a_changed_library_is_a_conflict()
    {
        _repository.SaveStrategyAsync(Arg.Any<Strategy>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(false);

        Should.Throw<StateConflictException>(() =>
            new CreateStrategyCommandHandler(_repository, _time).Handle(new CreateStrategyCommand("x", StrategyFixtures.Element("ema")), CancellationToken.None));
    }

    [Test]
    public async Task Change_replaces_the_strategy()
    {
        await new UpdateStrategyCommandHandler(_repository, _time).Handle(new UpdateStrategyCommand(Existing, "EMA 2", StrategyFixtures.Element("ema")), CancellationToken.None);

        await _repository.Received(1).SaveStrategyAsync(
            Arg.Is<Strategy>(strategy => strategy!.Id == Existing && strategy.Name == "EMA 2" && strategy.UpdatedAt == Now),
            7,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void Change_of_a_missing_strategy_is_not_found()
    {
        Should.Throw<NotFoundException>(() =>
            new UpdateStrategyCommandHandler(_repository, _time).Handle(new UpdateStrategyCommand(Guid.NewGuid(), "x", StrategyFixtures.Element("ema")), CancellationToken.None));
    }

    [Test]
    public async Task Strategy_is_deleted_at_the_loaded_revision()
    {
        await new DeleteStrategyCommandHandler(_repository).Handle(new DeleteStrategyCommand(Existing), CancellationToken.None);

        await _repository.Received(1).DeleteStrategyAsync(Existing, 7, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Deleting_a_missing_strategy_is_not_found()
    {
        Should.Throw<NotFoundException>(() => new DeleteStrategyCommandHandler(_repository).Handle(new DeleteStrategyCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Test]
    public void Delete_on_a_changed_library_is_a_conflict()
    {
        _repository.DeleteStrategyAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(false);

        Should.Throw<StateConflictException>(() => new DeleteStrategyCommandHandler(_repository).Handle(new DeleteStrategyCommand(Existing), CancellationToken.None));
    }

    [Test]
    public async Task Queries_read_strategies_from_the_repository()
    {
        Strategy strategy = _library.Strategies.Single();
        _repository.GetStrategyAsync(Existing, Arg.Any<CancellationToken>()).Returns(strategy);
        _repository.GetStrategiesAsync(Arg.Any<CancellationToken>()).Returns([strategy]);

        (await new GetStrategyQueryHandler(_repository).Handle(new GetStrategyQuery(Existing), CancellationToken.None)).ShouldBe(strategy);
        (await new GetStrategiesQueryHandler(_repository).Handle(new GetStrategiesQuery(), CancellationToken.None)).ShouldBe([strategy]);
    }
}