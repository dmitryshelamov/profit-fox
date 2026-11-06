import type { Schemas } from '@/shared/api/client';
import { roundDecimal, signed } from '@/shared/format/format';

import { dealsText } from './labels';

type Item = Schemas['IdeaAutoItemResponse'];
type Guards = Schemas['IdeaGuardsResponse'];

const none = { main: '—', sum: 'не было' };

/** The closed paper trades of an auto: their number with the mean result, and their sum in R. */
export function paperText(item: Pick<Item, 'paperClosed' | 'paperMeanR' | 'paperSumR'>) {
  if (Number(item.paperClosed) === 0 || item.paperMeanR === null) {
    return none;
  }

  return {
    main: `${item.paperClosed} · ср. ${signed(roundDecimal(item.paperMeanR, 2))} R`,
    sum: `итог ${signed(roundDecimal(item.paperSumR, 2))} R`,
  };
}

/** The closed real trades of an auto: their number, and their sum in USDT after the fees. */
export function realText(item: Pick<Item, 'realClosed' | 'realResult'>) {
  if (Number(item.realClosed) === 0) {
    return none;
  }

  return {
    main: dealsText(Number(item.realClosed)),
    sum: `${signed(roundDecimal(item.realResult, 2))} USDT`,
  };
}

/** The state of the guards for a line: each with its name and its value; a guard that is off says so. */
export function guardParts(guards: Guards): readonly { name: string; value: string }[] {
  const day = roundDecimal(guards.daySum, 2);
  const sides = `в лонг ${guards.openLongs} · в шорт ${guards.openShorts}`;

  return [
    { name: 'открыто', value: `${guards.openTrades} из ${guards.maxOpenTrades}` },
    { name: 'риск', value: `${roundDecimal(guards.openRisk, 2)} из ${guards.maxTotalRisk} USDT` },
    {
      name: 'сегодня',
      value:
        guards.dailyLossLimit === null
          ? `${day} USDT · выкл`
          : `${day} из ${guards.dailyLossLimit} USDT`,
    },
    {
      name: sides,
      value: guards.maxSameDirection === null ? 'выкл' : `из ${guards.maxSameDirection}`,
    },
  ];
}
