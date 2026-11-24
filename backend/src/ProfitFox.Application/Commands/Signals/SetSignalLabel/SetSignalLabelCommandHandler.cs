using System.Globalization;

using MediatR;

using ProfitFox.Application.Commands.Signals.Base;
using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Commands.Signals.SetSignalLabel;

/// <summary>
///     The text is 1 to 12 characters as the eye counts them, not the built-in label, and one of its kind among the labels
///     that are on; the timeframe is one of the five; a label that is turned on checks the pairs its graph names against the
///     listed ones. Every error comes at once.
/// </summary>
internal sealed class SetSignalLabelCommandHandler(ISignalRepository signals, ISignalLabelRepository labels, IInstrumentCatalog catalog)
    : IRequestHandler<SetSignalLabelCommand, SignalLabel>
{
    public const int MaxText = 12;

    /// <exception cref="Domain.Exceptions.SignalLibraryException">There is no such signal, or it does not read.</exception>
    /// <exception cref="IdeasRequestException">The label has an error.</exception>
    public async Task<SignalLabel> Handle(SetSignalLabelCommand request, CancellationToken cancellationToken)
    {
        SignalDocument signal = await SignalChecks.ReadAsync(signals, request.Signal, cancellationToken);
        List<PreviewErrorInternal> errors = [];
        string text = request.Text?.Trim() ?? "";
        int length = new StringInfo(text).LengthInTextElements;
        if (length is < 1 or > MaxText)
        {
            errors.Add(new PreviewErrorInternal("InvalidText", $"текст метки — от 1 до {MaxText} знаков") { Field = "text" });
        }
        else if (text == IdeasSettings.BurstLabel)
        {
            errors.Add(new PreviewErrorInternal("TakenText", "такая метка уже есть: это встроенная метка") { Field = "text" });
        }

        if (!Timeframes.TryParse(request.Timeframe, out TimeSpan timeframe))
        {
            errors.Add(new PreviewErrorInternal("InvalidTimeframe", $"таймфрейм метки — один из {Timeframes.Listed}") { Field = "timeframe" });
        }

        if (request.Enabled)
        {
            string[] listed = [.. (await catalog.LinearUsdtPerpetuals(cancellationToken)).Select(pair => pair.Symbol)];
            errors.AddRange(StrategyGraph.CheckNamedPairs(signal.Graph, listed).Select(PreviewErrorInternal.Of));
        }

        if (errors.Count > 0)
        {
            throw new IdeasRequestException(errors);
        }

        var label = new SignalLabel(signal.Id, request.Enabled, text, timeframe);
        if (!await labels.SetAsync(label, cancellationToken))
        {
            throw new IdeasRequestException([new PreviewErrorInternal("TakenText", $"такая метка уже есть: {text}") { Field = "text" }]);
        }

        return label;
    }
}
