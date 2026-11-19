namespace ProfitFox.Bench.Signals;

/// <summary>
///     Where a node stands on the canvas of the constructor. It is never part of the graph (spec signal-constructor
///     «Расположение узлов отдельно от графа»).
/// </summary>
public sealed record NodePosition(double X, double Y);
