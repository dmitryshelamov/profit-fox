import { ideaOf, marketRow, savedFilterOf } from '@/test/fixtures';

import { matchesIdea, matchesMarketRow } from './apply';

const filterOf = (...conditions: [string, string, string | null, (string | null)?][]) =>
  savedFilterOf({
    conditions: conditions.map(([field, operator, value, to]) => ({
      field,
      operator,
      value,
      to: to ?? null,
    })),
  });

describe('matchesMarketRow', () => {
  // Scenario "Выбор фильтра на рынке".
  it('keeps a pair that meets every condition, percent as the row has it and funding as a fraction', () => {
    const filter = savedFilterOf();

    expect(matchesMarketRow(filter, marketRow({ change1h: '3.5', fundingRate: '0.0004' }))).toBe(
      true,
    );
    expect(matchesMarketRow(filter, marketRow({ change1h: '2.9', fundingRate: '0.0004' }))).toBe(
      false,
    );
    expect(matchesMarketRow(filter, marketRow({ change1h: '3.5', fundingRate: '0.0002' }))).toBe(
      false,
    );
  });

  it.each([
    ['>', '5', null, '5.01', true],
    ['>', '5', null, '5', false],
    ['>=', '5', null, '5.0', true],
    ['<', '5', null, '4.99', true],
    ['<', '5', null, '5', false],
    ['<=', '5', null, '5', true],
    ['=', '5', null, '5.00', true],
    ['=', '5', null, '5.01', false],
    ['between', '1', '3', '1', true],
    ['between', '1', '3', '3', true],
    ['between', '1', '3', '3.01', false],
    ['between', '1', '3', '0.99', false],
  ])('tests %s %s %s against %s as %s', (operator, value, to, actual, expected) => {
    const filter = filterOf(['turnover24h', operator, value, to]);

    expect(matchesMarketRow(filter, marketRow({ turnover24h: actual }))).toBe(expected);
  });

  it('compares the digits exactly', () => {
    const filter = filterOf(['price', '>', '0.1234567890123456789']);

    expect(matchesMarketRow(filter, marketRow({ price: '0.1234567890123456790' }))).toBe(true);
    expect(matchesMarketRow(filter, marketRow({ price: '0.1234567890123456788' }))).toBe(false);
  });

  it('drops a pair that has no value for a field', () => {
    expect(
      matchesMarketRow(filterOf(['openInterest', '>', '1']), marketRow({ openInterest: null })),
    ).toBe(false);
  });

  // Conditions on idea fields act only on «Идеи».
  it('ignores the conditions on idea fields', () => {
    const filter = filterOf(['probability', '>', '0.9'], ['change1h', '>', '1']);

    expect(matchesMarketRow(filter, marketRow({ change1h: '2' }))).toBe(true);
    expect(matchesMarketRow(filterOf(['cases', '>', '1000']), marketRow())).toBe(true);
  });
});

describe('matchesIdea', () => {
  const row = marketRow({ symbol: 'WIFUSDT', turnover24h: '60000000' });

  it('tests the idea fields: R:R and the bucket of 90 days', () => {
    const idea = ideaOf();

    expect(matchesIdea(filterOf(['riskReward', '>=', '2']), idea, row)).toBe(true);
    expect(matchesIdea(filterOf(['probability', '>', '0.45']), idea, row)).toBe(true);
    expect(matchesIdea(filterOf(['probability', '>', '0.47']), idea, row)).toBe(false);
    expect(matchesIdea(filterOf(['expectancy', '>', '0.3']), idea, row)).toBe(true);
    expect(matchesIdea(filterOf(['cases', '>', '120']), idea, row)).toBe(false);
    expect(matchesIdea(filterOf(['cases', '>=', '120']), idea, row)).toBe(true);
  });

  it('drops an idea whose bucket has no case when a condition needs the bucket', () => {
    const idea = ideaOf({ bucket: null });

    expect(matchesIdea(filterOf(['expectancy', '>', '-5']), idea, row)).toBe(false);
    expect(matchesIdea(filterOf(['cases', '<', '5']), idea, row)).toBe(false);
  });

  it('drops an idea with too little data when a condition needs its probability', () => {
    const idea = ideaOf({
      bucket: { ...ideaOf().bucket!, enoughData: false, probability: null },
    });

    expect(matchesIdea(filterOf(['probability', '>', '0']), idea, row)).toBe(false);
  });

  it('tests «выше базовой» by the ▲ rule', () => {
    const below = ideaOf({
      bucket: { ...ideaOf().bucket!, probabilityLow: '0.28', probabilityHigh: '0.38' },
    });

    expect(matchesIdea(filterOf(['aboveBaseline', 'above_baseline', null]), ideaOf(), row)).toBe(
      true,
    );
    expect(matchesIdea(filterOf(['aboveBaseline', 'above_baseline', null]), below, row)).toBe(
      false,
    );
    expect(
      matchesIdea(
        filterOf(['aboveBaseline', 'above_baseline', null]),
        ideaOf({ bucket: null }),
        row,
      ),
    ).toBe(false);
  });

  it("tests the market fields on the row of the idea's pair", () => {
    const filter = filterOf(['turnover24h', '>', '50000000']);

    expect(matchesIdea(filter, ideaOf(), row)).toBe(true);
    expect(matchesIdea(filter, ideaOf(), marketRow({ turnover24h: '1' }))).toBe(false);
    expect(matchesIdea(filter, ideaOf(), undefined)).toBe(false);
  });

  it('needs every condition', () => {
    expect(
      matchesIdea(filterOf(['riskReward', '>=', '2'], ['cases', '>', '500']), ideaOf(), row),
    ).toBe(false);
  });
});
