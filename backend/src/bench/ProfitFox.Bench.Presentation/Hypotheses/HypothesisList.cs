using System.Text.Json;
using ProfitFox.Bench.Presentation.Preview;
using ProfitFox.Bench.Hypotheses;

namespace ProfitFox.Bench.Presentation.Hypotheses;

/// <summary>
///     The hypotheses of <c>bench/hypotheses</c> (spec signal-preview «Список гипотез»), ordered by folder. A folder whose
///     file has errors stays in the list with them; its id and name then come from the folder name.
/// </summary>
public static class HypothesisList
{
    public static IReadOnlyList<HypothesisResponse> Read(BenchFolder bench)
    {
        string root = Path.Combine(bench.Path, "hypotheses");
        if (!Directory.Exists(root))
        {
            return [];
        }

        return [.. Directory.GetDirectories(root).Order(StringComparer.Ordinal).Select(Item)];
    }

    private static HypothesisResponse Item(string folder)
    {
        try
        {
            Hypothesis hypothesis = HypothesisReader.Read(folder);
            using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, HypothesisReader.FileName)));
            JsonElement signal = file.RootElement.GetProperty("signal").Clone();
            return new HypothesisResponse(hypothesis.Id, hypothesis.Name, Timeframes.Name(hypothesis.Timeframe), signal, Error: null);
        }
        catch (HypothesisException exception)
        {
            string name = Path.GetFileName(folder);
            int dash = name.IndexOf('-', StringComparison.Ordinal);
            return new HypothesisResponse(
                dash > 0 ? name[..dash] : name,
                dash > 0 ? name[(dash + 1)..] : "",
                Timeframe: null,
                Signal: null,
                string.Join("; ", exception.Errors));
        }
    }
}
