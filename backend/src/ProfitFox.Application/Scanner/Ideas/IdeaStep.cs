using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>What a minute did to a pair: the setups that hold on it, the ideas it made and the open ideas it moved; in a replay, the signals it gave.</summary>
public sealed record IdeaStep(IReadOnlyList<PairSetup> Setups, IReadOnlyList<Idea> Created, IReadOnlyList<Idea> Changed, IReadOnlyList<IdeaSignal> Signals);