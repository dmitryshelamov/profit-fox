using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Scoring;
using ProfitFox.Tests.Unit.Application.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Commands.Scoring;

/// <summary>
///     The weights of the labels in memory, over the labels of <paramref name="labels" />: a row a text of the labels, the one
///     that is on first, named by <paramref name="names" /> or by the id of its signal.
/// </summary>
public sealed class MemoryLabelWeightRepository(MemorySignalLabelRepository labels, IReadOnlyDictionary<Guid, string>? names = null) : ILabelWeightRepository
{
    private readonly SortedDictionary<string, LabelWeight> _weights = new(StringComparer.Ordinal);

    public int Writes { get; private set; }

    public Task<IReadOnlyList<LabelWeight>> AllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<LabelWeight>>([.. _weights.Values]);
    }

    public Task SetAsync(LabelWeight weight, CancellationToken cancellationToken)
    {
        if (!LabelWeight.Valid(weight.Long) || !LabelWeight.Valid(weight.Short))
        {
            throw new InvalidOperationException("the database refuses a weight outside −100…100");
        }

        Writes++;
        _weights[weight.Text] = weight;
        return Task.CompletedTask;
    }

    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (_weights.TryGetValue(from, out LabelWeight? moved))
        {
            _weights.TryAdd(to, moved with { Text = to });
            if (!(await labels.AllAsync(cancellationToken)).Any(label => label.Text == from))
            {
                _weights.Remove(from);
            }
        }
    }

    public async Task<IReadOnlyList<ScoringLabel>> LabelsAsync(CancellationToken cancellationToken)
    {
        return
        [
            .. (await labels.AllAsync(cancellationToken))
                .OrderBy(label => label.Text, StringComparer.Ordinal)
                .ThenByDescending(label => label.Enabled)
                .ThenBy(Name, StringComparer.Ordinal)
                .DistinctBy(label => label.Text)
                .OrderByDescending(label => label.Enabled)
                .ThenBy(label => label.Text, StringComparer.Ordinal)
                .Select(label => new ScoringLabel(
                    label.Text,
                    label.Enabled,
                    Timeframes.Name(label.Timeframe),
                    Name(label),
                    _weights.GetValueOrDefault(label.Text)?.Long ?? 0,
                    _weights.GetValueOrDefault(label.Text)?.Short ?? 0))
        ];
    }

    private string Name(SignalLabel label)
    {
        return names?.GetValueOrDefault(label.SignalId) ?? label.SignalId.ToString();
    }
}
