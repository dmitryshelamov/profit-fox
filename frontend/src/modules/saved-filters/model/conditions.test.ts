import { savedFilterOf } from '@/test/fixtures';

import {
  conditionText,
  fieldLabel,
  fromTyped,
  operatorLabel,
  readyConditions,
  toTyped,
  unitLabel,
} from './conditions';

const condition = (
  field: string,
  operator: string,
  value: string | null,
  to: string | null = null,
) => ({
  field,
  operator,
  value,
  to,
});

describe('typed values', () => {
  it('shows the fractions of funding and probability in percent and takes percent back', () => {
    expect(toTyped('fundingRate', '0.0003')).toBe('0.03');
    expect(fromTyped('fundingRate', '0.03')).toBe('0.0003');
    expect(toTyped('probability', '0.45')).toBe('45');
    expect(fromTyped('probability', '45')).toBe('0.45');
  });

  it('leaves the other fields as they are', () => {
    expect(toTyped('change1h', '3')).toBe('3');
    expect(fromTyped('turnover24h', '50000000')).toBe('50000000');
  });

  it('takes a decimal comma for a point', () => {
    expect(fromTyped('change1h', '3,5')).toBe('3.5');
    expect(fromTyped('fundingRate', '0,03')).toBe('0.0003');
  });

  it('leaves a text that is not a number for the server to refuse', () => {
    expect(fromTyped('fundingRate', 'abc')).toBe('abc');
    expect(fromTyped('fundingRate', '')).toBe('');
  });
});

describe('labels', () => {
  it('names fields, operators and units', () => {
    expect(fieldLabel('change1h')).toBe('Изменение 1ч');
    expect(fieldLabel('aboveBaseline')).toBe('Выше базовой линии');
    expect(operatorLabel('>=')).toBe('≥');
    expect(operatorLabel('between')).toBe('между');
    expect(unitLabel('turnover24h')).toBe('USDT');
    expect(unitLabel('change1h')).toBe('%');
    expect(unitLabel('fundingRate')).toBe('%');
    expect(unitLabel('expectancy')).toBe('R');
    expect(unitLabel('cases')).toBe('');
  });
});

describe('conditionText', () => {
  it('writes a comparison with its value and unit', () => {
    expect(conditionText(condition('change1h', '>', '3'))).toBe('Изменение 1ч > 3 %');
    expect(conditionText(condition('fundingRate', '>', '0.0003'))).toBe('Фандинг > 0.03 %');
    expect(conditionText(condition('turnover24h', '>=', '50000000'))).toBe(
      'Оборот 24ч ≥ 50 000 000 USDT',
    );
  });

  it('writes a range and a flag', () => {
    expect(conditionText(condition('change5m', 'between', '1', '3'))).toBe(
      'Изменение 5м между 1 и 3 %',
    );
    expect(conditionText(condition('aboveBaseline', 'above_baseline', null))).toBe(
      'Выше базовой линии',
    );
    expect(conditionText(condition('cases', '>', '30'))).toBe('Случаев (N) > 30');
  });
});

describe('readyConditions', () => {
  it('keeps the conditions a filter can test now', () => {
    const filter = savedFilterOf({
      conditions: [
        condition('change1h', '>', '3'),
        condition('change5m', '>', ''),
        condition('change15m', 'between', '1', ''),
        condition('aboveBaseline', 'above_baseline', null),
        condition('spread', '<', 'abc'),
      ],
    });

    expect(readyConditions(filter.conditions).map((item) => item.field)).toEqual([
      'change1h',
      'aboveBaseline',
    ]);
  });
});
