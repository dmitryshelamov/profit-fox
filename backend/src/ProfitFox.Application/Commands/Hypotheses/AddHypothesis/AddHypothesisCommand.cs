using MediatR;

namespace ProfitFox.Application.Commands.Hypotheses.AddHypothesis;

/// <summary>
///     Loads the text of a <c>hypothesis.json</c> as a version of its hypothesis (spec hypothesis-bench «Заморозка гипотезы»):
///     a text with errors is refused and not kept.
/// </summary>
public sealed record AddHypothesisCommand(string Text) : IRequest<AddHypothesisResult>;
