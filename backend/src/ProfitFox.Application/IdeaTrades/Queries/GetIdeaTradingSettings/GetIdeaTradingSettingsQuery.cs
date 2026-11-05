using MediatR;

using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaTradingSettings;

/// <summary>The settings of trading by ideas; the defaults while none were saved.</summary>
public sealed record GetIdeaTradingSettingsQuery : IRequest<IdeaTradingSettings>;