namespace ProfitFox.Domain.Verdicts;

/// <summary>
///     A verdict and the numbers behind it. <see cref="T" />, <see cref="Mean" /> and the halves are null when the outcomes
///     of the events were not read.
/// </summary>
public sealed record Verdict(
    VerdictKind Kind,
    string Reason,
    int Attempts,
    double RequiredT,
    double MinEffect,
    int Blocks,
    int Events,
    double Power,
    double PseudoSd,
    double? T = null,
    double? Mean = null,
    double? FirstHalfMean = null,
    double? SecondHalfMean = null);
