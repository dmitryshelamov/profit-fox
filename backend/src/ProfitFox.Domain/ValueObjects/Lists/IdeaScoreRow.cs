namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A row of the outcomes by score (change add-idea-score, spec idea-score «Итоги по баллу»): a score, its closed ideas, the
///     share of those in the plus, the mean outcome after costs as a fraction and the sum in USDT.
/// </summary>
public sealed record IdeaScoreRow(int Score, int Ideas, double Wins, double Mean, double Sum);
