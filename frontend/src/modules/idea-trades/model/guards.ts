import type { Schemas } from '@/shared/api/client';
import { compareDecimals } from '@/shared/format/decimal';
import { formatMoment, roundDecimal } from '@/shared/format/format';

type Guards = Schemas['IdeaGuardsResponse'];

export const openStateText = (guards: Guards) =>
  `сейчас открыто ${guards.openTrades} из ${guards.maxOpenTrades}`;

export const riskStateText = (guards: Guards) =>
  `сейчас ${roundDecimal(guards.openRisk, 2)} из ${guards.maxTotalRisk} USDT`;

/** What the day has come to, against the daily stop when it is on. */
export function dayStateText(guards: Guards): string {
  const day = roundDecimal(guards.daySum, 2);

  return guards.dailyLossLimit === null
    ? `Сегодня: ${day} USDT · защита выключена`
    : `Сегодня: ${day} из ${guards.dailyLossLimit} USDT`;
}

/** The share of the limit of the daily stop that the loss of the day has used, from 0 to 1; none when the stop is off. The one place the digits become numbers: for the width of a bar. */
export function dayShare(guards: Guards): number | null {
  if (guards.dailyLossLimit === null) {
    return null;
  }

  if (compareDecimals(guards.daySum, '0') >= 0) {
    return 0;
  }

  return Math.min(1, Math.abs(Number(guards.daySum)) / Number(guards.dailyLossLimit));
}

/** What the open real trades of the autos take of the budget of all the autos, when it is on. */
export function autosBudgetStateText(guards: Guards): string {
  const used = roundDecimal(guards.autosBudgetUsed, 2);

  return guards.autosBudget === null
    ? `сейчас ${used} USDT · защита выключена`
    : `сейчас ${used} из ${guards.autosBudget} USDT`;
}

export const sidesStateText = (guards: Guards) =>
  `открытых реальных: в лонг ${guards.openLongs} · в шорт ${guards.openShorts}${
    guards.maxSameDirection === null ? ' · защита выключена' : ` из ${guards.maxSameDirection}`
  }`;

/** When the autos enter again, while the daily stop holds. */
export function resumeText(guards: Guards): string | null {
  return guards.triggeredAt === null || guards.resumesAt === null
    ? null
    : `автоматы снова входят с ${formatMoment(guards.resumesAt)}`;
}
