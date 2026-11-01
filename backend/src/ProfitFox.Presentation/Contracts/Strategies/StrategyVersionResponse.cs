using System.Text.Json.Nodes;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

/// <param name="Number"></param>
/// <param name="CreatedAt"></param>
/// <param name="Definition">The strategy as the version executes it, without layout.</param>
/// <param name="Composites">Every composite node the version uses, built-in ones included.</param>
public sealed record StrategyVersionResponse(int Number, DateTimeOffset CreatedAt, JsonObject Definition, IReadOnlyList<VersionNodeResponse> Composites)
{
    public static StrategyVersionResponse Of(StrategyVersion version)
    {
        return new StrategyVersionResponse(
            version.Number,
            version.CreatedAt,
            StrategyJson.WriteStrategy(version.Snapshot.Definition),
            [.. version.Snapshot.Composites.Select(composite => new VersionNodeResponse(StrategyJson.ToCode(composite.Id), StrategyJson.WriteComposite(composite.Definition)))]);
    }
}