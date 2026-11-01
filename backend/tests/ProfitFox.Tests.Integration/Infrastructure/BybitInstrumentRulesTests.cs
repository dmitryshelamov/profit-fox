using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Requirement "Правила инструмента": the rules come from Bybit instruments-info, through the replaced HTTP of the app.</summary>
public class BybitInstrumentRulesTests
{
    private ProfitFoxFactory _factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT"), ("linear", "BTCUSDT")]);
        _factory.Bybit.Rules[("spot", "BTCUSDT")] = ("0.01", "0.000001", "0.000048", "1");
        _factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.10", "0.001", "0.001", "5");
        _factory.CreateClient().Dispose();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Spot_rules_are_the_tick_the_base_precision_and_the_minimum_quantity_and_amount()
    {
        InstrumentRules rules = await Provider().GetRulesAsync(new Instrument(MarketCategory.Spot, "BTCUSDT"), CancellationToken.None);

        rules.ShouldBe(new InstrumentRules(0.01m, 0.000001m, 0.000048m, 1m));
    }

    [Test]
    public async Task Linear_rules_are_the_tick_the_quantity_step_and_the_minimum_quantity_and_notional()
    {
        InstrumentRules rules = await Provider().GetRulesAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), CancellationToken.None);

        rules.ShouldBe(new InstrumentRules(0.10m, 0.001m, 0.001m, 5m));
    }

    // Scenario "Биржа недоступна".
    [Test]
    public async Task Unavailable_bybit_means_the_exchange_is_unavailable()
    {
        _factory.Bybit.Unavailable = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => Provider().GetRulesAsync(new Instrument(MarketCategory.Spot, "BTCUSDT"), CancellationToken.None));
    }

    [Test]
    public async Task Instrument_bybit_does_not_list_has_no_rules()
    {
        await Should.ThrowAsync<ExchangeException>(() => Provider().GetRulesAsync(new Instrument(MarketCategory.Spot, "NOPEUSDT"), CancellationToken.None));
    }

    private IInstrumentRulesProvider Provider()
    {
        return _factory.Services.GetRequiredService<IInstrumentRulesProvider>();
    }
}