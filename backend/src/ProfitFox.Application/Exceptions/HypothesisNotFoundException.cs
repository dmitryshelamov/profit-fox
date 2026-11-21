using ProfitFox.Domain.Exceptions;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The store has no such hypothesis, or no such version of it.
/// </summary>
public sealed class HypothesisNotFoundException(string id, int? version)
    : HypothesisException(id, [version is null ? "there is no such hypothesis" : $"there is no version {version}"]);
