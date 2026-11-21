using System.Text.Json;

using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Hypotheses;

/// <summary>
///     The hypotheses of the bench: one version read and checked for a run, all the latest versions for the page (spec
///     signal-preview «Список гипотез»), and a text loaded as a new version (spec hypothesis-bench «Заморозка гипотезы»). A
///     hypothesis whose text has errors stays in the list with them.
/// </summary>
public sealed class HypothesisCatalog(IHypothesisStore store)
{
    /// <summary>
    ///     The version <paramref name="version" /> of the hypothesis <paramref name="id" />, or its latest.
    /// </summary>
    /// <exception cref="HypothesisNotFoundException">The hypothesis or its version is not there.</exception>
    /// <exception cref="HypothesisException">The text of the version cannot be run.</exception>
    public async Task<Hypothesis> Read(string id, int? version, CancellationToken cancellation)
    {
        StoredHypothesis stored = await store.Find(id, version, cancellation)
                                  ?? throw new HypothesisNotFoundException(id, version);
        return Parse(stored);
    }

    public async Task<IReadOnlyList<HypothesisItem>> List(CancellationToken cancellation)
    {
        return [.. (await store.Latest(cancellation)).Select(Item)];
    }

    public Task<IReadOnlyList<HypothesisKey>> Keys(CancellationToken cancellation)
    {
        return store.Keys(cancellation);
    }

    /// <summary>
    ///     Loads a text: a text with errors is refused and not kept.
    /// </summary>
    /// <exception cref="HypothesisException">The text cannot be run.</exception>
    public async Task<HypothesisAdded> Add(string text, CancellationToken cancellation)
    {
        string normalized = HypothesisReader.Normalize(text);
        Hypothesis hypothesis = HypothesisReader.Parse(normalized);
        (int version, bool created) = await store.Add(hypothesis.Id, hypothesis.Hash, normalized, cancellation);
        return new HypothesisAdded(hypothesis with { Version = version }, created);
    }

    private static Hypothesis Parse(StoredHypothesis stored)
    {
        Hypothesis hypothesis = HypothesisReader.Parse(stored.Text, stored.Version);
        return hypothesis.Id == stored.Id ? hypothesis : throw new HypothesisException(stored.Id, [$"the text is of {hypothesis.Id}"]);
    }

    private static HypothesisItem Item(StoredHypothesis stored)
    {
        try
        {
            Hypothesis hypothesis = Parse(stored);
            using var file = JsonDocument.Parse(stored.Text);
            JsonElement signal = file.RootElement.GetProperty("signal").Clone();
            return new HypothesisItem(hypothesis.Id, hypothesis.Version, hypothesis.Name, hypothesis.Timeframe, signal, Error: null);
        }
        catch (HypothesisException exception)
        {
            return new HypothesisItem(stored.Id, stored.Version, NameOf(stored.Text), Timeframe: null, Signal: null, string.Join("; ", exception.Errors));
        }
    }

    private static string NameOf(string text)
    {
        try
        {
            using var file = JsonDocument.Parse(text);
            return file.RootElement.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
