using MediatR;

using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Commands.SaveIdeaTradingSettings;

/// <summary>Replaces the settings of trading by ideas.</summary>
public sealed record SaveIdeaTradingSettingsCommand(IdeaTradingSettings Settings) : IRequest;