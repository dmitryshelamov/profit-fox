using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>The autos of the scanner with no auto on and no paper trade open but the ones given, for the tests that are not about them.</summary>
public static class NoAutos
{
    public static LiveAutos Create(
        ScannerMarket market,
        BacktestSettings fees,
        TimeProvider? time = null,
        IIdeaAutoEntryRepository? entries = null,
        IIdeaAutoRefusalRepository? refusals = null,
        IReadOnlyList<PaperIdeaTrade>? open = null)
    {
        IIdeaAutoRepository autos = Substitute.For<IIdeaAutoRepository>();
        autos.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns([]);
        IPaperIdeaTradeRepository paper = Substitute.For<IPaperIdeaTradeRepository>();
        paper.GetOpenAsync(Arg.Any<CancellationToken>()).Returns(open ?? new List<PaperIdeaTrade>());

        return new LiveAutos(
            autos,
            paper,
            Substitute.For<IIdeaTradeRepository>(),
            Substitute.For<IIdeaTradingSettingsRepository>(),
            Substitute.For<IIdeaBucketRepository>(),
            Substitute.For<ICandleRepository>(),
            entries ?? Substitute.For<IIdeaAutoEntryRepository>(),
            refusals ?? Substitute.For<IIdeaAutoRefusalRepository>(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            market,
            fees,
            new WorkerIdentity(Guid.NewGuid()),
            time ?? new FakeTimeProvider());
    }
}