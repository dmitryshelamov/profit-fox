using MediatR;

using ProfitFox.Domain.ValueObjects.Scoring;

namespace ProfitFox.Application.Commands.Scoring.SetLabelWeight;

/// <summary>
///     Sets the weights of the label with the text <see cref="Text" /> (change add-idea-score, spec idea-score «Вес метки»;
///     design.md, decision 3): a whole number from −3 to +3 for a long and one for a short. The answer is the row of the label
///     with its new weights.
/// </summary>
public sealed record SetLabelWeightCommand(string? Text, decimal? LongWeight, decimal? ShortWeight) : IRequest<ScoringLabel>;
