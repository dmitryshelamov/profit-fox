using ProfitFox.Domain.ValueObjects.Graph;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     A problem with the settings of a backtest and where it is: the field of the request, the parameter or the pair. An
///     error of the graph keeps its code and its place in <see cref="Graph" />.
/// </summary>
public sealed record BacktestError(string Code, string Reason)
{
    public const string InvalidTimeframe = "InvalidTimeframe";
    public const string InvalidDate = "InvalidDate";
    public const string EndAfterToday = "EndAfterToday";
    public const string EmptyHistory = "EmptyHistory";
    public const string UnknownInstrument = "UnknownInstrument";
    public const string PairsRequired = "PairsRequired";
    public const string TooManyPairs = "TooManyPairs";
    public const string InvalidFee = "InvalidFee";
    public const string TooManySets = "TooManySets";
    public const string NoRest = "NoRest";

    public string? Field { get; init; }

    public string? Parameter { get; init; }

    public string? Pair { get; init; }

    public GraphError? Graph { get; init; }

    /// <summary>
    ///     An error of a graph, with its code and place.
    /// </summary>
    public static BacktestError Of(GraphError error)
    {
        return new BacktestError(error.Code.ToString(), error.Reason) { Field = error.Field, Parameter = error.Parameter, Graph = error };
    }
}
