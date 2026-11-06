using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetExitRulePreview;

/// <summary>The preview of a candidate exit rule of a type × interval against the rule in force (decision 8).</summary>
public sealed record GetExitRulePreviewQuery(string? Type, string? Interval, int Life, decimal? Target, int? Window) : IRequest<ExitRulePreview>;