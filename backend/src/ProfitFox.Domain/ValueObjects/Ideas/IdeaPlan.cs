using ProfitFox.Domain.Entities.Ideas.Enums;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The levels of an idea: where it enters, where it stops out and where it takes its profit.</summary>
public sealed record IdeaPlan(IdeaType Type, IdeaSide Side, decimal Entry, decimal Stop, decimal Target)
{
    /// <summary>The reward to the risk.</summary>
    public decimal RiskReward => Math.Abs(Target - Entry) / Math.Abs(Entry - Stop);
}