import { type FieldErrors, layOutProblems, type ProblemErrors } from '@/shared/api/problems';

import type { SettingsInput } from '../api';

/** The names of the fields of the settings in the API and in the form. */
const fields: ReadonlySet<string> = new Set([
  'accountId',
  'amount',
  'maxAmount',
  'maxOpenTrades',
  'maxTotalRisk',
  'minRiskReward',
  'dailyLossLimit',
  'maxSameDirection',
  'autosBudget',
  'leverage',
]);

/** The settings the API gave, every number as text for the form; no account is an empty choice. */
export function formOfSettings(settings: {
  accountId: string | null;
  amount: string;
  maxAmount: string;
  maxOpenTrades: number | string;
  maxTotalRisk: string;
  minRiskReward: string;
  dailyLossLimit: string | null;
  maxSameDirection: number | string | null;
  autosBudget: string | null;
  leverage: number | string;
}): { [K in keyof SettingsInput]: string } {
  return {
    accountId: settings.accountId ?? '',
    amount: settings.amount,
    maxAmount: settings.maxAmount,
    maxOpenTrades: String(settings.maxOpenTrades),
    maxTotalRisk: settings.maxTotalRisk,
    minRiskReward: settings.minRiskReward,
    dailyLossLimit: settings.dailyLossLimit ?? '',
    maxSameDirection: settings.maxSameDirection === null ? '' : String(settings.maxSameDirection),
    autosBudget: settings.autosBudget ?? '',
    leverage: String(settings.leverage),
  };
}

/** The errors of the API laid out by the field of the settings that failed. */
export function settingsFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key));
}
