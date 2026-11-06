using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;

/// <summary>The report asked for, none before the first calibration, with the window and the category it was asked by; or validation errors.</summary>
public sealed record GetIdeaCalibrationResult(int Days, MarketCategory? Category, CalibrationReport? Report, IReadOnlyDictionary<string, string> Errors);