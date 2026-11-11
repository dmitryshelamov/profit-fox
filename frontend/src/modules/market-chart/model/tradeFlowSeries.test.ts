import { tradeFlowSeries } from './tradeFlowSeries';

const bar = (openTime: string, buyTurnover: string, sellTurnover: string) => ({
  openTime,
  buyTurnover,
  sellTurnover,
});

describe('tradeFlowSeries', () => {
  it('draws the buys up, the sells down and the CVD from zero at the start of the period', () => {
    const series = tradeFlowSeries([
      bar('2026-11-11T12:00:00Z', '451', '447'),
      bar('2026-11-11T12:15:00Z', '100', '150.5'),
    ]);
    const start = Date.parse('2026-11-11T12:00:00Z') / 1000;

    expect(series.buys).toEqual([
      { time: start, value: 451 },
      { time: start + 900, value: 100 },
    ]);
    expect(series.sells).toEqual([
      { time: start, value: -447 },
      { time: start + 900, value: -150.5 },
    ]);
    expect(series.cvd).toEqual([
      { time: start, value: 4 },
      { time: start + 900, value: -46.5 },
    ]);
  });

  // Scenario "Смена интервала": the CVD starts again from zero at the start of the bars it is given.
  it('starts the CVD again for the bars of another interval', () => {
    const hourly = tradeFlowSeries([bar('2026-11-11T12:00:00Z', '551', '597.5')]);

    expect(hourly.cvd).toEqual([{ time: Date.parse('2026-11-11T12:00:00Z') / 1000, value: -46.5 }]);
  });
});
