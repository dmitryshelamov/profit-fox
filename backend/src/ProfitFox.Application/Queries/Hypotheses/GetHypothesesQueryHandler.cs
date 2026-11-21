using System.Text.Json;

using MediatR;

using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Queries.Hypotheses;

internal sealed class GetHypothesesQueryHandler(IHypothesisRepository hypotheses) : IRequestHandler<GetHypothesesQuery, IReadOnlyList<HypothesisInternal>>
{
    public async Task<IReadOnlyList<HypothesisInternal>> Handle(GetHypothesesQuery request, CancellationToken cancellationToken)
    {
        return [.. (await hypotheses.LatestAsync(cancellationToken)).Select(Item)];
    }

    private static HypothesisInternal Item(StoredHypothesis stored)
    {
        try
        {
            Hypothesis hypothesis = HypothesisReader.Parse(stored);
            using var file = JsonDocument.Parse(stored.Text);
            JsonElement signal = file.RootElement.GetProperty("signal").Clone();
            return new HypothesisInternal(hypothesis.Id, hypothesis.Version, hypothesis.Name, hypothesis.Timeframe, signal, Error: null);
        }
        catch (HypothesisException exception)
        {
            return new HypothesisInternal(stored.Id, stored.Version, NameOf(stored.Text), Timeframe: null, Signal: null, string.Join("; ", exception.Errors));
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
