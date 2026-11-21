using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Application.Commands.Base;

/// <summary>
///     The rule for the name of a saved signal or strategy, which is one (spec strategy-constructor «Сохранённая стратегия»):
///     <see cref="SignalNames" />. The names of signals and strategies do not depend on each other.
/// </summary>
internal static class DocumentNames
{
    /// <summary>
    ///     The error of a name against the rule; <paramref name="what" /> is «signal» or «strategy».
    /// </summary>
    public static GraphError Error(string name, string what)
    {
        return new GraphError(
            GraphErrorCode.InvalidField,
            $"{name} is not a {what} name: lower-case latin letters and digits joined by single dashes, at most {SignalNames.MaxLength} characters")
        {
            Field = "name"
        };
    }
}
