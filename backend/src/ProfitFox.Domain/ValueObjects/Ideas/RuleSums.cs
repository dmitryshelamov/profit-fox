namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The entries and their total R of a rule of the grid in the out-of-sample check: in each of the 6 blocks, apart in its body and in its
///     tail, the last horizon of the rule; and on the selection and the check.
/// </summary>
public sealed record RuleSums(BlockSums[] Bodies, BlockSums[] Tails, BlockSums Selection, BlockSums Check);