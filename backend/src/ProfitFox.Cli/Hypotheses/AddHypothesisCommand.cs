using System.Net;

using ProfitFox.Cli.Api;

namespace ProfitFox.Cli.Hypotheses;

/// <summary>
///     <c>bench hypotheses add</c>: loads a <c>hypothesis.json</c> into the bench as a new version, or finds the version that has
///     this text.
/// </summary>
internal sealed class AddHypothesisCommand(BenchClient client, TextWriter output, TextWriter error)
{
    public async Task<int> Execute(string file, CancellationToken cancellation)
    {
        if (!File.Exists(file))
        {
            await error.WriteLineAsync($"there is no file {file}.");
            return 1;
        }

        Answer<HypothesisAddedDto> answer = await client.AddHypothesis(await File.ReadAllBytesAsync(file, cancellation), cancellation);
        if (answer.Value is not { } added)
        {
            await error.WriteLineAsync($"refused: {answer.Reason}");
            return 1;
        }

        await output.WriteLineAsync(answer.Status == HttpStatusCode.Created
            ? $"{added.Id} version {added.Version} created"
            : $"{added.Id} already exists as version {added.Version}");
        return 0;
    }
}
