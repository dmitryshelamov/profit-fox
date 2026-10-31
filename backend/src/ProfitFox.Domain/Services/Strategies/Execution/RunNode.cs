using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>A simple node of the expanded graph; composite nodes are gone, their inputs and outputs nodes relay values.</summary>
internal sealed record RunNode(string Path, SimpleNodeKind Kind, NodeSettings Settings, IReadOnlyDictionary<string, Source> Inputs);