import type { DeclaredParameter } from './parameterValues';

/** A parameter as the definition of a strategy declares it. */
export interface ParameterDef extends DeclaredParameter {
  readonly default?: number | string | boolean;
  readonly min?: number | string;
  readonly max?: number | string;
  readonly integer?: boolean;
}

/** `целое · 2…200`: the kind of a parameter and its limits. */
export function parameterMeta(parameter: ParameterDef): string {
  const kind =
    parameter.type === 'duration' ? 'длительность' : parameter.integer ? 'целое' : 'число';
  const limits =
    parameter.min !== undefined && parameter.max !== undefined
      ? ` · ${parameter.min}…${parameter.max}`
      : '';

  return `${kind}${limits}`;
}
