using ProfitFox.Application.Commands.Hypotheses;
using ProfitFox.Application.Contracts.Hypotheses;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses;

/// <summary>
///     Spec hypothesis-bench «Заморозка гипотезы»: the versions of a hypothesis in the database. A text becomes a version,
///     the same text is the same version, an edit is the next one, and nothing that was kept changes.
/// </summary>
public sealed class HypothesisStoreTests
{
    private readonly HypothesisCatalog _hypotheses = new HypothesisCatalog(new HypothesisStore(BenchDatabase.DataSource));

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task The_first_text_of_an_id_is_version_1_with_the_hash_of_the_ledger()
    {
        HypothesisAdded added = await Add(BenchRepository.H0001Text());

        Assert.That((added.Created, added.Hypothesis.Version), Is.EqualTo((true, 1)));
        Assert.That(added.Hypothesis.Hash, Does.StartWith("5591d11a"));
    }

    [Test]
    public async Task An_edit_after_a_run_is_version_2_and_version_1_stays_as_it_was()
    {
        HypothesisAdded first = await Add(BenchRepository.H0001Text());
        string edited = BenchRepository.H0001Text().Replace("0.0005", "0.001", StringComparison.Ordinal);

        HypothesisAdded second = await Add(edited);

        Assert.That((second.Created, second.Hypothesis.Version), Is.EqualTo((true, 2)));
        Hypothesis one = await _hypotheses.Read("H0001", version: 1, CancellationToken.None);
        Assert.That(one.Hash, Is.EqualTo(first.Hypothesis.Hash));
        Assert.That((await _hypotheses.Read("H0001", version: null, CancellationToken.None)).Hash, Is.EqualTo(second.Hypothesis.Hash), "the latest is the default");
        Assert.That((await _hypotheses.Keys(CancellationToken.None)).Select(key => (key.Id, key.Version)), Is.EqualTo(new[] { ("H0001", 1), ("H0001", 2) }));
    }

    [Test]
    public async Task The_same_text_makes_no_version_and_points_at_the_one_it_is()
    {
        await Add(BenchRepository.H0001Text());
        await Add(BenchRepository.H0001Text().Replace("0.0005", "0.001", StringComparison.Ordinal));

        HypothesisAdded again = await Add(BenchRepository.H0001Text());

        Assert.That((again.Created, again.Hypothesis.Version), Is.EqualTo((false, 1)));
        Assert.That(await _hypotheses.Keys(CancellationToken.None), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Windows_line_ends_make_the_same_version()
    {
        await Add(BenchRepository.H0001Text());

        HypothesisAdded windows = await Add(BenchRepository.H0001Text().Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.That((windows.Created, windows.Hypothesis.Version), Is.EqualTo((false, 1)));
    }

    [Test]
    public async Task A_text_with_errors_is_refused_and_not_kept()
    {
        string broken = BenchRepository.H0001Text().Replace("\"measure\": \"net\"", "\"measure\": \"r\"", StringComparison.Ordinal);

        HypothesisException error = Assert.ThrowsAsync<HypothesisException>(() => Add(broken))!;

        Assert.That(error.Errors, Is.EqualTo(new[] { "measure r needs a stop in the plan" }));
        Assert.That(await _hypotheses.Keys(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task Texts_of_one_id_loaded_together_take_different_versions()
    {
        string text = BenchRepository.H0001Text();
        string[] texts = [.. Enumerable.Range(1, 6).Select(number => text.Replace("0.0005", $"0.00{number}", StringComparison.Ordinal))];

        HypothesisAdded[] added = await Task.WhenAll(texts.Select(Add));

        Assert.That(added.Select(item => item.Hypothesis.Version).Order(), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
    }

    [Test]
    public async Task The_list_gives_the_latest_version_of_every_hypothesis_in_the_order_of_the_ids()
    {
        await Add(BenchRepository.H0001Text());
        await Add(BenchRepository.H0001Text().Replace("0.0005", "0.001", StringComparison.Ordinal));
        await Add(BenchRepository.H0001Text().Replace("\"H0001\"", "\"H0002\"", StringComparison.Ordinal));

        IReadOnlyList<HypothesisItem> list = await _hypotheses.List(CancellationToken.None);

        Assert.That(list.Select(item => (item.Id, item.Version, item.Error)), Is.EqualTo(new[] { ("H0001", 2, (string?)null), ("H0002", 1, null) }));
        Assert.That(list[0].Signal, Is.Not.Null);
    }

    [Test]
    public void A_hypothesis_that_is_not_there_is_refused_by_name()
    {
        Assert.That(() => _hypotheses.Read("H0009", version: null, CancellationToken.None), Throws.InstanceOf<HypothesisException>().With.Message.Contains("H0009"));
    }

    private Task<HypothesisAdded> Add(string text)
    {
        return _hypotheses.Add(text, CancellationToken.None);
    }
}
