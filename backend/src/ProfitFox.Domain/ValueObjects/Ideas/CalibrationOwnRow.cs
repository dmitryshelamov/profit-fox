namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     A row of the table of calibration by own data: the checked cases whose bucket had 30 cases of its own and more, or fewer, «мало своих
///     данных», as a row by badge, with the Brier score of their promised probability.
/// </summary>
public sealed record CalibrationOwnRow(bool FewOwnCases, CalibrationRow Row, decimal Brier);