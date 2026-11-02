import { isDecimalText, jsonNumberOf } from '@/shared/api/jsonNumber';

import { rangeCount } from './grid';

/** What the form holds for one parameter: a single value, or a range with its step. All of it is text. */
export interface ParameterInput {
  readonly mode: 'value' | 'range';
  readonly value: string;
  readonly min: string;
  readonly max: string;
  readonly step: string;
}

export const valueInput: ParameterInput = { mode: 'value', value: '', min: '', max: '', step: '' };

/** A count of values, or why there is none; no reason while the range is still being typed. */
export type ValueCount = { readonly count: bigint } | { readonly problem: string | undefined };

const countable = /^-?\d+(\.\d+)?$|^(\d+\.)?\d+:\d+:\d+$/;

const problems = {
  step: () => 'Шаг должен быть больше нуля.',
  order: () => 'min больше max.',
  divisibility: (step: string) => `max − min не делится на шаг ${step}.`,
};

export function valueCountOf(input: ParameterInput): ValueCount {
  if (input.mode === 'value') {
    return { count: 1n };
  }

  if (![input.min, input.max, input.step].every((text) => countable.test(text))) {
    return { problem: undefined };
  }

  const counted = rangeCount(input);

  return 'count' in counted ? counted : { problem: problems[counted.problem](input.step) };
}

/** The number of backtests of the form, or the first parameter whose range cannot be split into values. */
export function combinationTotal(
  counts: readonly { readonly name: string; readonly count: ValueCount }[],
): { readonly total: bigint } | { readonly unresolved: string } {
  let total = 1n;

  for (const { name, count } of counts) {
    if (!('count' in count)) {
      return { unresolved: name };
    }

    total *= count.count;
  }

  return { total };
}

type Declared = { readonly name: string; readonly type: 'number' | 'boolean' | 'duration' };

const typed = (type: Declared['type'], text: string) =>
  type === 'number' && isDecimalText(text) ? jsonNumberOf(text) : text;

/** The `parameters` of the request: filled values and ranges; an empty value is left out, so the bot's value applies. */
export function parametersBody(
  declared: readonly Declared[],
  inputs: Readonly<Record<string, ParameterInput>>,
): Record<string, unknown> {
  const body: Record<string, unknown> = {};

  for (const { name, type } of declared) {
    const input = inputs[name] ?? valueInput;

    if (input.mode === 'range') {
      body[name] = {
        min: typed(type, input.min.trim()),
        max: typed(type, input.max.trim()),
        step: typed(type, input.step.trim()),
      };
    } else if (input.value.trim() !== '') {
      body[name] = typed(type, input.value.trim());
    }
  }

  return body;
}
