namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     What a fill did to the position of a cycle (spec strategy-preview «Циклы в ответе предпросмотра»): opened it,
///     increased it, decreased it, or closed it.
/// </summary>
public enum FillAction
{
    Entry,
    Increase,
    Decrease,
    Exit
}
