using System.Text.Json;
using ProfitFox.Bench.Domain.Hypotheses;

namespace ProfitFox.Bench.Application.Hypotheses;

/// <summary>
///     The hypotheses of the bench: one read and checked for a run, or all of them for the page (spec signal-preview
///     «Список гипотез»), ordered by folder. A hypothesis whose file has errors stays in the list with them; its id and
///     name then come from the folder name.
/// </summary>
public sealed class HypothesisCatalog(IHypothesisStore store)
{
    /// <exception cref="HypothesisException">The hypothesis is not there or cannot be run.</exception>
    public Hypothesis Read(string folder)
    {
        return HypothesisReader.Parse(folder, store.Text(folder));
    }

    public IReadOnlyList<HypothesisItem> List()
    {
        return [.. store.Folders().Order(StringComparer.Ordinal).Select(Item)];
    }

    private HypothesisItem Item(string folder)
    {
        try
        {
            string text = store.Text(folder);
            Hypothesis hypothesis = HypothesisReader.Parse(folder, text);
            using JsonDocument file = JsonDocument.Parse(text);
            JsonElement signal = file.RootElement.GetProperty("signal").Clone();
            return new HypothesisItem(hypothesis.Id, hypothesis.Name, hypothesis.Timeframe, signal, Error: null);
        }
        catch (HypothesisException exception)
        {
            string name = Path.GetFileName(folder);
            int dash = name.IndexOf('-', StringComparison.Ordinal);
            return new HypothesisItem(
                dash > 0 ? name[..dash] : name,
                dash > 0 ? name[(dash + 1)..] : "",
                Timeframe: null,
                Signal: null,
                string.Join("; ", exception.Errors));
        }
    }
}
