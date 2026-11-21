using MediatR;

using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Commands.Hypotheses.AddHypothesis;

/// <summary>
///     The text is kept with <c>\n</c> line ends, so a file saved on Windows is the version of the same file saved elsewhere.
/// </summary>
internal sealed class AddHypothesisCommandHandler(IHypothesisRepository hypotheses) : IRequestHandler<AddHypothesisCommand, AddHypothesisResult>
{
    /// <exception cref="HypothesisException">The text cannot be run.</exception>
    public async Task<AddHypothesisResult> Handle(AddHypothesisCommand request, CancellationToken cancellationToken)
    {
        string normalized = HypothesisReader.Normalize(request.Text);
        Hypothesis hypothesis = HypothesisReader.Parse(normalized);
        (int version, bool created) = await hypotheses.AddAsync(hypothesis.Id, hypothesis.Hash, normalized, cancellationToken);
        return new AddHypothesisResult(hypothesis.Id, hypothesis.Name, version, created);
    }
}
