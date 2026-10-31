namespace ProfitFox.Domain.Entities.Strategies.Enums;

/// <summary>Kind of a built-in node with its own logic.</summary>
public enum SimpleNodeKind
{
    Parameters,
    Inputs,
    Outputs,
    Candle,
    Position,
    Ema,
    Cross,
    Add,
    Subtract,
    Multiply,
    Divide,
    Compare,
    And,
    Or,
    Not,
    MarketOrder,
    ClosePosition,
    HoldLimit
}