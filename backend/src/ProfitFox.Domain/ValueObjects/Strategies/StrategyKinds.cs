using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;

namespace ProfitFox.Domain.ValueObjects.Strategies;

/// <summary>
///     The kinds of a strategy as the API, the store and the files write them: <c>bot</c> and <c>idea</c>.
/// </summary>
public static class StrategyKinds
{
    public const string Bot = "bot";
    public const string Idea = "idea";

    public static string Name(StrategyKind kind)
    {
        return kind switch
        {
            StrategyKind.Bot => Bot,
            StrategyKind.Idea => Idea,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A strategy is for a bot or a category of ideas.")
        };
    }

    public static bool TryParse(string? name, out StrategyKind kind)
    {
        kind = name == Idea ? StrategyKind.Idea : StrategyKind.Bot;
        return name is Bot or Idea;
    }

    /// <summary>
    ///     The error of a field <c>kind</c> that is neither <c>bot</c> nor <c>idea</c>.
    /// </summary>
    public static GraphError Invalid(string? name)
    {
        return new GraphError(GraphErrorCode.InvalidField, $"the kind of a strategy is {Bot} or {Idea}, not {name}") { Field = "kind" };
    }

    /// <summary>
    ///     The error of a save that would change the kind of the strategy <paramref name="strategy" />, whose kind is
    ///     <paramref name="kind" /> (spec strategy-constructor «Вид при создании стратегии»).
    /// </summary>
    public static GraphError Changed(string strategy, StrategyKind kind)
    {
        return new GraphError(GraphErrorCode.InvalidField, $"the kind of the strategy {strategy} is {Name(kind)}: it is chosen when the strategy is created and does not change")
        {
            Field = "kind"
        };
    }
}
