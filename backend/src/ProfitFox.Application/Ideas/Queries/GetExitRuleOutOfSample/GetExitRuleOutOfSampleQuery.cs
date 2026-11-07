using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetExitRuleOutOfSample;

/// <summary>The out-of-sample check of a candidate exit rule of a type × interval (decision 2 of check-idea-exit-rules-out-of-sample).</summary>
public sealed record GetExitRuleOutOfSampleQuery(string? Type, string? Interval, int Life, decimal? Target, int? Window) : IRequest<ExitRuleOutOfSample>;