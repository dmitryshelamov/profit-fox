using MediatR;

namespace ProfitFox.Application.Ideas.Queries.GetIdeaCalibration;

/// <summary>The report of the calibration of a window of days, 90 by default or 30, and of a category, all of them without one. Raw parameters as the user sent them.</summary>
public sealed record GetIdeaCalibrationQuery(string? Days, string? Category) : IRequest<GetIdeaCalibrationResult>;