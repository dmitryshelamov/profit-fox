using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;

/// <summary>The report asked for, none before the first calibration, with the window, the category and the scope it was asked by; or validation errors.</summary>
public sealed record GetIdeaCalibrationResult(int Days, MarketCategory? Category, CalibrationScope Scope, CalibrationReport? Report, IReadOnlyDictionary<string, string> Errors);