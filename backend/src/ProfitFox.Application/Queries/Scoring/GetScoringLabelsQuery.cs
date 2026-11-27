using MediatR;

using ProfitFox.Domain.ValueObjects.Scoring;

namespace ProfitFox.Application.Queries.Scoring;

/// <summary>
///     The labels of «Скоринг» with their weights (change add-idea-score, spec idea-score «Раздел «Скоринг»»; design.md,
///     decision 3): a row a text of the labels of signals, those that are on first, then the built-in label.
/// </summary>
public sealed record GetScoringLabelsQuery : IRequest<IReadOnlyList<ScoringLabel>>;
