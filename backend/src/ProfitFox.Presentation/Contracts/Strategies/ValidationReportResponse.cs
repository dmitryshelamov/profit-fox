using ProfitFox.Application.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <summary>What saving would reject; all lists are empty when the save would pass.</summary>
/// <param name="Errors">Errors of the name and the definition, keyed by path as in a rejected save.</param>
/// <param name="Usages">Graphs a node change would break.</param>
/// <param name="Bots">Bots a strategy change would break.</param>
public sealed record ValidationReportResponse(
    IReadOnlyDictionary<string, string[]> Errors,
    IReadOnlyList<UsageResponse> Usages,
    IReadOnlyList<AffectedBotResponse> Bots)
{
    public static ValidationReportResponse Of(ValidationReport report)
    {
        return new ValidationReportResponse(
            report.Errors.ToDictionary(error => error.Key, error => new[] { error.Value }),
            [.. report.Usages.Select(UsageResponse.Of)],
            [.. report.Bots.Select(AffectedBotResponse.Of)]);
    }
}