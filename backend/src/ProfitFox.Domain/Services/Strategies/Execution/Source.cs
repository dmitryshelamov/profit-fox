using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Where an input of an expanded node takes its value: a value set on the input or an output of another node.</summary>
internal sealed record Source(NodeValue? Literal, int Node = -1, string Pin = "");