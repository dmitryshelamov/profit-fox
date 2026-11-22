using MediatR;

using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Commands.Signals.SetSignalLabel;

/// <summary>
///     Sets the label of the saved signal <see cref="Signal" /> (change add-ideas-and-market, spec idea-labels «Метка
///     сигнала»): whether it is on, its text and its timeframe.
/// </summary>
public sealed record SetSignalLabelCommand(string Signal, bool Enabled, string? Text, string? Timeframe) : IRequest<SignalLabel>;
