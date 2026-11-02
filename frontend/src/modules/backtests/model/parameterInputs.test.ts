import { jsonNumberOf } from '@/shared/api/jsonNumber';

import {
  combinationTotal,
  type ParameterInput,
  parametersBody,
  valueCountOf,
  valueInput,
} from './parameterInputs';

const range = (min: string, max: string, step: string): ParameterInput => ({
  mode: 'range',
  value: '',
  min,
  max,
  step,
});

describe('values of a parameter', () => {
  it('counts one value for a single value, filled or not', () => {
    expect(valueCountOf(valueInput)).toEqual({ count: 1n });
    expect(valueCountOf({ ...valueInput, value: '3.5' })).toEqual({ count: 1n });
  });

  it('counts the values of a range', () => {
    expect(valueCountOf(range('5', '15', '5'))).toEqual({ count: 3n });
    expect(valueCountOf(range('01:00:00', '03:00:00', '01:00:00'))).toEqual({ count: 3n });
  });

  it('has no count and no message while the range is incomplete or is not a number', () => {
    expect(valueCountOf(range('5', '', '5'))).toEqual({ problem: undefined });
    expect(valueCountOf(range('5', 'x', '5'))).toEqual({ problem: undefined });
  });

  it.each([
    [range('5', '15', '0'), 'Шаг должен быть больше нуля.'],
    [range('15', '5', '5'), 'min больше max.'],
    [range('20', '45', '10'), 'max − min не делится на шаг 10.'],
  ])('explains a range that cannot be split into values', (input, problem) => {
    expect(valueCountOf(input)).toEqual({ problem });
  });
});

describe('combinations of the form', () => {
  it('multiplies the counts of the parameters', () => {
    expect(
      combinationTotal([
        { name: 'fast', count: { count: 3n } },
        { name: 'slow', count: { count: 4n } },
        { name: 'takeProfit', count: { count: 1n } },
      ]),
    ).toEqual({ total: 12n });
  });

  it('names the first parameter whose range has no count', () => {
    expect(
      combinationTotal([
        { name: 'fast', count: { count: 3n } },
        { name: 'slow', count: { problem: 'max − min не делится на шаг 10.' } },
        { name: 'cooldown', count: { problem: undefined } },
      ]),
    ).toEqual({ unresolved: 'slow' });
  });
});

describe('parameters of the request', () => {
  const declared = [
    { name: 'fast', type: 'number' as const },
    { name: 'slow', type: 'number' as const },
    { name: 'cooldown', type: 'duration' as const },
  ];

  it('sends the filled values as numbers or durations and leaves the empty ones out', () => {
    expect(
      parametersBody(declared, {
        fast: { ...valueInput, value: '10' },
        slow: valueInput,
        cooldown: { ...valueInput, value: '02:00:00' },
      }),
    ).toEqual({ fast: jsonNumberOf('10'), cooldown: '02:00:00' });
  });

  it('sends a range as min, max and step', () => {
    expect(
      parametersBody(declared, {
        fast: range('5', '15', '5'),
        cooldown: range('01:00:00', '03:00:00', '01:00:00'),
      }),
    ).toEqual({
      fast: { min: jsonNumberOf('5'), max: jsonNumberOf('15'), step: jsonNumberOf('5') },
      cooldown: { min: '01:00:00', max: '03:00:00', step: '01:00:00' },
    });
  });

  it('sends text that is no number as it is, so that the server reports the field', () => {
    expect(
      parametersBody(declared, {
        fast: { ...valueInput, value: 'abc' },
        slow: range('5', 'x', '5'),
      }),
    ).toEqual({
      fast: 'abc',
      slow: { min: jsonNumberOf('5'), max: 'x', step: jsonNumberOf('5') },
    });
  });
});
