using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Tests.Integration.Exchange;

/// <summary>
///     Change take-idea-trades, task 5.7 (design.md, decision 15): the fake exchange itself — the stop and the target fill when
///     the price passes them, a repeated link id makes no second position, a liquidation and an ADL give a fill without an
///     order, and the stream sends the events, drops, keeps silent and comes back.
/// </summary>
public sealed class FakeBybitTests
{
    private const string Key = "key-main";
    private static readonly ExchangeCredentials Credentials = new ExchangeCredentials(Key, "secret");

    private FakeBybit _bybit = null!;
    private readonly List<ExchangeMessage> _messages = [];
    private readonly List<ExchangeStreamState> _states = [];

    [SetUp]
    public void Start()
    {
        _bybit = new FakeBybit().Pair("SOLUSDT", 237.40m);
        _messages.Clear();
        _states.Clear();
    }

    [Test]
    public async Task The_stop_of_the_position_fills_when_the_price_passes_it()
    {
        await Enter(stop: 231.38m, target: 245.54m);

        _bybit.Price("SOLUSDT", 231.10m);

        ExchangeFill stop = _bybit.FillsOf(Key)[^1];
        Assert.That((stop.Side, stop.Price, stop.Quantity, stop.StopType, stop.OrderLinkId), Is.EqualTo((TradeSide.Short, 231.38m, 0.4m, ExchangeStopType.StopLoss, (string?)null)));
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT"), Is.Null);
    }

    [Test]
    public async Task The_target_of_the_position_fills_when_the_price_reaches_it()
    {
        await Enter(stop: 231.38m, target: 245.54m);

        _bybit.Price("SOLUSDT", 240m);
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT"), Is.Not.Null);
        _bybit.Price("SOLUSDT", 245.60m);

        Assert.That(_bybit.FillsOf(Key)[^1].StopType, Is.EqualTo(ExchangeStopType.TakeProfit));
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT"), Is.Null);
    }

    [Test]
    public async Task A_repeated_link_id_makes_no_second_position()
    {
        Assert.That((await Enter()).Kind, Is.EqualTo(ExchangeAnswerKind.Accepted));
        Assert.That((await Enter()).Kind, Is.EqualTo(ExchangeAnswerKind.Duplicate));

        Assert.That(_bybit.PositionOf(Key, "SOLUSDT")!.Size, Is.EqualTo(0.4m));
    }

    [Test]
    public async Task A_lost_answer_still_takes_the_order()
    {
        _bybit.Key(Key).Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));

        Assert.That((await Enter()).Kind, Is.EqualTo(ExchangeAnswerKind.Temporary));
        Assert.That((await Enter()).Kind, Is.EqualTo(ExchangeAnswerKind.Duplicate));
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT")!.Size, Is.EqualTo(0.4m));
    }

    [TestCase(false, ExchangeFillType.Liquidation)]
    [TestCase(true, ExchangeFillType.Adl)]
    public async Task A_liquidation_and_an_ADL_give_a_fill_without_an_order(bool adl, ExchangeFillType type)
    {
        await Enter();

        _bybit.Liquidate(Key, "SOLUSDT", 158.3m, adl);

        ExchangeFill fill = _bybit.FillsOf(Key)[^1];
        Assert.That((fill.Type, fill.OrderLinkId, fill.Price), Is.EqualTo((type, (string?)null, 158.3m)));
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT"), Is.Null);
    }

    [Test]
    public async Task The_stream_sends_the_order_the_fill_and_the_position_drops_keeps_silent_and_comes_back()
    {
        IExchangeSubscription stream = await _bybit.OpenAsync(Credentials, _messages.Add, _states.Add, CancellationToken.None);

        await Enter();
        Assert.That(_messages.Select(message => message.GetType().Name), Is.EqualTo(new[] { "ExchangeOrderUpdate", "ExchangeFill", "ExchangePosition", "ExchangeOrderUpdate" }));

        _bybit.Key(Key).Drop();
        _bybit.Price("SOLUSDT", 220m);
        _bybit.Key(Key).Restore();
        Assert.That(_states, Is.EqualTo(new[] { ExchangeStreamState.Lost, ExchangeStreamState.Restored }));
        Assert.That(_messages, Has.Count.EqualTo(4), "nothing of the time without the connection");
        Assert.That((await _bybit.FillsAsync(Credentials, DateTimeOffset.MinValue, CancellationToken.None)).Count, Is.EqualTo(2), "but REST has it");

        _bybit.Key(Key).Silent = 1;
        _bybit.ByHand(Key, "SOLUSDT", TradeSide.Long, 1m);
        Assert.That(_messages.OfType<ExchangeFill>().Count(), Is.EqualTo(1), "the live stream kept silent about the fill");
        Assert.That(_messages[^1], Is.InstanceOf<ExchangePosition>());

        _bybit.Key(Key).Close();
        Assert.That(_states[^1], Is.EqualTo(ExchangeStreamState.Closed));
        await stream.DisposeAsync();
        Assert.That(_bybit.Key(Key).OpenStreams, Is.Zero);
    }

    // Review R8: the fake refuses what Bybit refuses — a quantity off the step or below the least, a stop or a target off the
    // tick or on the wrong side of the price, a leverage above the greatest — and a key that does not answer writes nothing.
    [Test]
    public async Task It_refuses_what_Bybit_refuses_and_writes_nothing_without_an_answer()
    {
        static ExchangeOrderRequest Entry(decimal quantity, decimal? stop, decimal? target)
        {
            return new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, quantity, $"t0a1b2c3d4e5f6g7h8i9j0k1l{quantity * 100 % 10}-1", stop, target, false);
        }

        Assert.That((await _bybit.PlaceOrderAsync(Credentials, Entry(0.45m, 231.38m, null), CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "off the step");
        Assert.That((await _bybit.PlaceOrderAsync(Credentials, Entry(0.4m, 231.385m, null), CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "stop off the tick");
        Assert.That((await _bybit.PlaceOrderAsync(Credentials, Entry(0.4m, 240m, null), CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "stop above a long");
        Assert.That((await _bybit.PlaceOrderAsync(Credentials, Entry(0.4m, 231.38m, 230m), CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "target below a long");
        Assert.That((await _bybit.SetLeverageAsync(Credentials, "SOLUSDT", 51, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "leverage above the max");
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT"), Is.Null);

        await Enter(stop: 231.38m);
        Assert.That((await _bybit.SetStopAsync(Credentials, "SOLUSDT", 240m, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Refused), "a stop above the price of a long");
        _bybit.Key(Key).Unavailable = true;
        Assert.That((await _bybit.SetStopAsync(Credentials, "SOLUSDT", 232m, CancellationToken.None)).Kind, Is.EqualTo(ExchangeAnswerKind.Temporary));
        Assert.That(_bybit.PositionOf(Key, "SOLUSDT")!.Stop, Is.EqualTo(231.38m));
    }

    [Test]
    public async Task The_snapshot_of_a_pair_in_the_hedge_mode_says_so()
    {
        _bybit.Key(Key).Hedge.Add("SOLUSDT");
        await Enter();

        Assert.That((await _bybit.PositionsAsync(Credentials, null, CancellationToken.None)).Single().OneWay, Is.False);
    }

    [Test]
    public async Task A_fill_can_come_before_its_order_and_twice()
    {
        _bybit.Key(Key).FillBeforeOrder = true;
        _bybit.Key(Key).FillTwice = true;
        await _bybit.OpenAsync(Credentials, _messages.Add, _states.Add, CancellationToken.None);

        await Enter();

        Assert.That(_messages.Select(message => message.GetType().Name), Is.EqualTo(new[] { "ExchangeFill", "ExchangeFill", "ExchangePosition", "ExchangeOrderUpdate" }));
    }

    [Test]
    public async Task The_mark_goes_to_the_stream_of_the_prices_of_the_pair()
    {
        var marks = new List<(string, decimal)>();
        await _bybit.OpenAsync("SOLUSDT", (symbol, mark) => marks.Add((symbol, mark)), _ => { }, CancellationToken.None);

        _bybit.Price("SOLUSDT", 238m);

        Assert.That(marks, Is.EqualTo(new[] { ("SOLUSDT", 238m) }));
    }

    private Task<ExchangeAnswer> Enter(decimal? stop = 231.38m, decimal? target = null)
    {
        return _bybit.PlaceOrderAsync(Credentials, new ExchangeOrderRequest("SOLUSDT", TradeSide.Long, 0.4m, "t0a1b2c3d4e5f6g7h8i9j0k1l2-1", stop, target, false), CancellationToken.None);
    }
}
