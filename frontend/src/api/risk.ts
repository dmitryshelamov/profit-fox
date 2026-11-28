import { api, type Schemas, unwrap } from './client';
import { type Written, written } from './signals';

/** The state of the risk module (change take-idea-trades, spec trade-risk «Раздел „Риск“»). */
export type RiskState = Schemas['RiskStateResponse'];
export type Halt = Schemas['HaltResponse'];
/** The settings of the risk module (spec trade-risk «Настройки риска», «Сроки и попытки»). */
export type RiskSettings = Schemas['TradeSettingsResponse'];
export type RiskSettingsRequest = Schemas['TradeSettingsRequest'];

/** The key of the state of the risk module. */
export const riskStateKey = ['risk', 'state'] as const;
/** The key of the settings of the risk module. */
export const riskSettingsKey = ['risk', 'settings'] as const;

export async function getRiskState(): Promise<RiskState> {
  return unwrap(await api.GET('/api/risk/state'));
}

export async function getRiskSettings(): Promise<RiskSettings> {
  return unwrap(await api.GET('/api/risk/settings'));
}

/** Saves the settings; a refused field comes back as an error at it. */
export async function saveRiskSettings(
  request: RiskSettingsRequest,
): Promise<Written<RiskSettings>> {
  return written(await api.PUT('/api/risk/settings', { body: request }));
}

/** «Снять запрет»: refused (409) while a trade still closes, with the reason. */
export async function liftHalt(): Promise<
  { readonly ok: true } | { readonly ok: false; readonly reason: string }
> {
  const result = await api.POST('/api/risk/halt/lift');
  if (result.response.ok) {
    return { ok: true };
  }

  const problem = (result.error ?? {}) as { detail?: string; title?: string };
  return { ok: false, reason: problem.detail ?? problem.title ?? result.response.statusText };
}

/** Half the way to the liquidation in percent at the leverage, cut to a tenth: `16.6` at 3×, `5` at 10×. */
export function halfWayText(leverage: number): string {
  const half = Math.trunc((100 / (2 * leverage)) * 10) / 10;
  return String(half);
}

/** How often the state of the risk module is read again (spec idea-trades «Раздел „Сделки“»: at least every 5 s). */
export const riskRefreshMs = 5_000;
