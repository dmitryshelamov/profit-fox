namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The exit rule to apply: the lifetime in candles, the target in R of a type with one, the entry window of a pullback and a stop entry.</summary>
public sealed record ApplyExitRuleBody(int Life, decimal? Target, int? Window);