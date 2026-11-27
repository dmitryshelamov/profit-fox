namespace ProfitFox.Application.Exceptions;

/// <summary>
///     No label of a signal has the text, and it is not the built-in label (change add-idea-score, spec idea-score «Текст,
///     которого нет»): weights are set only to labels there are. Nothing was written.
/// </summary>
public sealed class LabelNotFoundException(string text) : Exception($"нет метки «{text}»")
{
    public string Text { get; } = text;
}
