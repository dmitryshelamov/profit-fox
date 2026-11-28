using System.Text.Json;
using System.Text.Json.Serialization;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Converters.SystemTextJson;

using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Infrastructure.Clients.Bybit;

namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Change take-idea-trades, task 5.6 (design.md, decision 3): a message of each topic of the private stream — <c>order</c>,
///     <c>execution</c>, <c>position</c> — read by the package as its socket reads it and translated to the message of the port
///     field for field as the same record read by REST (task 5.3); a record of another category is not passed on; the events
///     of the connection are passed on once each. The connection itself is not tested here: task 11.5 checks it at Bybit.
/// </summary>
/// <remarks>
///     The package reads the data of a message with its own source-generated context of JSON, which it does not make public;
///     the test takes it by reflection to read the samples exactly as the socket does, without a socket.
/// </remarks>
public sealed class BybitStreamMessagesTests
{
    private static readonly JsonSerializerOptions Package = SerializerOptions.WithConverters(
        (JsonSerializerContext)Activator.CreateInstance(typeof(BybitSocketClient).Assembly.GetType("Bybit.Net.Converters.BybitSourceGenerationContext", throwOnError: true)!)!);

    [Test]
    public async Task A_message_of_the_execution_topic_is_the_fill_read_by_REST()
    {
        ExchangeMessage[] stream = [.. BybitAccountStream.Messages(Data<BybitUserTradeUpdate>(BybitSamples.Topic("execution", BybitSamples.EntryFill, BybitSamples.StopFill, BybitSamples.BustFill, BybitSamples.FundingFill)))];

        Assert.That(stream, Is.EqualTo(await Rest(stub => stub.Json(BybitSamples.List("linear", "", BybitSamples.EntryFill, BybitSamples.StopFill, BybitSamples.BustFill, BybitSamples.FundingFill)), (client, credentials) => client.FillsAsync(credentials, DateTimeOffset.FromUnixTimeMilliseconds(1795874400000).AddMinutes(-1), CancellationToken.None))));
        Assert.That(stream, Has.Length.EqualTo(4));
    }

    [Test]
    public async Task A_message_of_the_position_topic_is_the_position_read_by_REST()
    {
        ExchangeMessage[] stream = [.. BybitAccountStream.Messages(Data<BybitPositionUpdate>(BybitSamples.Topic("position", BybitSamples.Position, BybitSamples.EmptyPosition)))];

        Assert.That(stream, Is.EqualTo(await Rest(stub => stub.Json(BybitSamples.List("linear", "", BybitSamples.Position, BybitSamples.EmptyPosition)), (client, credentials) => client.PositionsAsync(credentials, null, CancellationToken.None))));
    }

    [Test]
    public async Task A_message_of_the_order_topic_is_the_order_read_by_REST()
    {
        ExchangeMessage[] stream = [.. BybitAccountStream.Messages(Data<BybitOrderUpdate>(BybitSamples.Topic("order", BybitSamples.Order)))];

        ExchangeOrderUpdate? rest = await Rest(stub => stub.Json(BybitSamples.List("linear", "", BybitSamples.Order)), (client, credentials) => client.OrderAsync(credentials, "SOLUSDT", BybitSamples.LinkId, CancellationToken.None));
        Assert.That(stream, Is.EqualTo(new[] { rest }));
    }

    [Test]
    public void A_record_of_another_category_is_not_passed_on()
    {
        string spot = BybitSamples.Topic("execution", BybitSamples.EntryFill).Replace("\"category\":\"linear\"", "\"category\":\"spot\"");
        Assert.That(BybitAccountStream.Messages(Data<BybitUserTradeUpdate>(spot)), Is.Empty);
    }

    [Test]
    public async Task The_events_of_the_connection_are_passed_on_once_each_and_nothing_after_the_close()
    {
        var states = new List<ExchangeStreamState>();
        var client = new Disposable();
        var subscription = new BybitStreamSubscription(client, states.Add);

        foreach (ExchangeStreamState state in new[] { ExchangeStreamState.Lost, ExchangeStreamState.Lost, ExchangeStreamState.Lost, ExchangeStreamState.Restored, ExchangeStreamState.Restored, ExchangeStreamState.Lost, ExchangeStreamState.Closed, ExchangeStreamState.Restored })
        {
            subscription.Tell(state);
        }

        Assert.That(states, Is.EqualTo(new[] { ExchangeStreamState.Lost, ExchangeStreamState.Restored, ExchangeStreamState.Lost, ExchangeStreamState.Closed }));
        await subscription.DisposeAsync();
        await subscription.DisposeAsync();
        Assert.That(client.Disposed, Is.EqualTo(1));
    }

    private static T[] Data<T>(string message)
    {
        using var document = JsonDocument.Parse(message);
        return JsonSerializer.Deserialize<T[]>(document.RootElement.GetProperty("data").GetRawText(), Package)!;
    }

    private static async Task<T> Rest<T>(Action<StubHttpHandler> answers, Func<BybitTradingClient, ExchangeCredentials, Task<T>> read)
    {
        using var stub = new StubHttpHandler();
        answers(stub);
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1795882000000));
        return await read(new BybitTradingClient(stub.Client(), new BybitSettings(), clock: clock), new ExchangeCredentials("key", "secret"));
    }

    private sealed class Disposable : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose()
        {
            Disposed++;
        }
    }
}
