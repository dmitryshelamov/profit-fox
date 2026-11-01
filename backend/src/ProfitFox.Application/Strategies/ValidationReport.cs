using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.ValueObjects.Bots;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.Strategies;

/// <summary>Everything saving a strategy or composite node would reject, found without saving it.</summary>
/// <param name="Errors">Errors of the name and the definition, keyed as in a rejected save.</param>
/// <param name="Usages">Graphs a node change would break.</param>
/// <param name="Bots">Bots a strategy change would break.</param>
public sealed record ValidationReport(IReadOnlyDictionary<string, string> Errors, IReadOnlyList<NodeUsage> Usages, IReadOnlyList<AffectedBot> Bots)
{
    /// <summary>Runs the checks of a save and turns its rejection into a report. Other failures pass through.</summary>
    public static ValidationReport Of(Action check)
    {
        try
        {
            check();

            return new ValidationReport(new Dictionary<string, string>(), [], []);
        }
        catch (ValidationFailedException exception)
        {
            return new ValidationReport(exception.Errors, [], []);
        }
        catch (StateConflictException exception) when (exception.Usages.Count > 0 || exception.Bots is { Count: > 0 })
        {
            return new ValidationReport(new Dictionary<string, string>(), exception.Usages, exception.Bots ?? []);
        }
    }
}