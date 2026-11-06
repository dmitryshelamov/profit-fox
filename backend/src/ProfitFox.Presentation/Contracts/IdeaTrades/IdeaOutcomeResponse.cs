using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>How the idea of a trade ended: the status it closed with (<c>target</c>, <c>stop</c> or <c>expired</c>), when, and its result in R as a decimal string, none for an idea that never entered.</summary>
public sealed record IdeaOutcomeResponse([property: Required] string Status, DateTimeOffset StatusAt, string? Result)
{
    public static IdeaOutcomeResponse? Of(TradedIdeaOutcome? outcome)
    {
        return outcome is null ? null : new IdeaOutcomeResponse(outcome.Status.ToCode(), outcome.StatusAt, outcome.Result is { } result ? IdeaTradeResponse.Text(result) : null);
    }
}