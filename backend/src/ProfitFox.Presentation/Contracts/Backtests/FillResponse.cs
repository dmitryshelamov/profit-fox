namespace ProfitFox.Presentation.Contracts.Backtests;

/// <param name="At"></param>
/// <param name="Price"></param>
/// <param name="Quantity"></param>
/// <param name="Fee"></param>
/// <param name="FeeAsset"><c>quote</c> or <c>base</c>.</param>
public sealed record FillResponse(DateTimeOffset At, string Price, string Quantity, string Fee, string FeeAsset);