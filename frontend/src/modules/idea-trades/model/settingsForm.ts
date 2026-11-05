import { type FieldErrors, layOutProblems, type ProblemErrors } from '@/shared/api/problems';

import type { SettingsInput } from '../api';

/** The names of the fields of the settings in the API and in the form. */
const fields: ReadonlySet<string> = new Set([
  'accountId',
  'risk',
  'maxAmount',
  'maxOpenTrades',
  'maxTotalRisk',
  'minRiskReward',
]);

/** The settings the API gave, every number as text for the form; no account is an empty choice. */
export function formOfSettings(settings: {
  accountId: string | null;
  risk: string;
  maxAmount: string;
  maxOpenTrades: number | string;
  maxTotalRisk: string;
  minRiskReward: string;
}): { [K in keyof SettingsInput]: string } {
  return {
    accountId: settings.accountId ?? '',
    risk: settings.risk,
    maxAmount: settings.maxAmount,
    maxOpenTrades: String(settings.maxOpenTrades),
    maxTotalRisk: settings.maxTotalRisk,
    minRiskReward: settings.minRiskReward,
  };
}

/** The errors of the API laid out by the field of the settings that failed. */
export function settingsFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key));
}
