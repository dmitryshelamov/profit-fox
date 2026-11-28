namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A row of the outcomes by score (change add-idea-score, spec idea-score «Итоги по баллу»): a group of scores from
///     <see cref="From" />, a multiple of 0.5, up to but not including <see cref="To" />, half a point higher (change
///     decimal-label-weights, design.md, decision 4); its closed ideas, the share of those in the plus, the mean outcome after
///     costs as a fraction and the sum in USDT.
/// </summary>
public sealed record IdeaScoreRow(decimal From, decimal To, int Ideas, double Wins, double Mean, double Sum);
