using MediatR;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Commands.ApplyExitRule;

/// <summary>Applies an exit rule to a type × interval: the ideas made from now on live by it, and the scanner recomputes its buckets.</summary>
public sealed record ApplyExitRuleCommand(string? Type, string? Interval, int Life, decimal? Target, int? Window) : IRequest<IdeaExitRule>;