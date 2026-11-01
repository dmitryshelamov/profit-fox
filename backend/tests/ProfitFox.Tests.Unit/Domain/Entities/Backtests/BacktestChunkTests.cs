using ProfitFox.Domain.Entities.Backtests;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Backtests;

/// <summary>Requirement "Куски бэктеста".</summary>
public class BacktestChunkTests
{
    // Scenario "Деление на куски".
    [Test]
    public void Grid_is_split_into_chunks_of_the_given_size_and_the_last_one_is_shorter()
    {
        var backtest = Guid.NewGuid();

        IReadOnlyList<BacktestChunk> chunks = BacktestChunk.Split(backtest, 12, 5);

        chunks.Select(chunk => (chunk.BacktestId, chunk.Number, chunk.First, chunk.Combinations))
            .ShouldBe([(backtest, 0, 0L, 5), (backtest, 1, 5L, 5), (backtest, 2, 10L, 2)]);
    }

    [Test]
    public void Grid_smaller_than_a_chunk_is_one_chunk()
    {
        IReadOnlyList<BacktestChunk> chunks = BacktestChunk.Split(Guid.NewGuid(), 1, 100);

        chunks.Select(chunk => (chunk.First, chunk.Combinations)).ShouldBe([(0L, 1)]);
    }
}