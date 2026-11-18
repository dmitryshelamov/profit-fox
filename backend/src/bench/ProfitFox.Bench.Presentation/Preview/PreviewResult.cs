using ProfitFox.Bench.Presentation.Errors;

namespace ProfitFox.Bench.Presentation.Preview;

/// <summary>
///     A preview, or the problems that refused its request.
/// </summary>
public sealed record PreviewResult(PreviewResponse? Preview, ErrorResponse? Refused);
