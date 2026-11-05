import { probabilityView } from '@/modules/ideas';
import type { MarketRow } from '@/modules/market';
import type { Schemas } from '@/shared/api/client';
import { compareDecimals } from '@/shared/format/decimal';

export type SavedFilter = Schemas['SavedFilterResponse'];
export type Condition = Schemas['SavedFilterConditionBody'];
export type Idea = Schemas['IdeaResponse'];

const ideaFields = new Set(['riskReward', 'probability', 'expectancy', 'cases', 'aboveBaseline']);

export const isIdeaField = (code: string | null) => code !== null && ideaFields.has(code);

/** A comparison of a saved filter always has its value, «между» also its upper bound: the server keeps no other. */
function holds(condition: Condition, actual: string | null): boolean {
  if (actual === null) {
    return false;
  }

  const against = compareDecimals(actual, condition.value!);

  switch (condition.operator) {
    case '>':
      return against > 0;
    case '>=':
      return against >= 0;
    case '<':
      return against < 0;
    case '<=':
      return against <= 0;
    case '=':
      return against === 0;
    default:
      return against >= 0 && compareDecimals(actual, condition.to!) <= 0;
  }
}

const marketValue = (row: MarketRow, field: string): string | null =>
  (row as unknown as Record<string, string | null>)[field] ?? null;

function ideaValue(idea: Idea, field: string): string | null {
  switch (field) {
    case 'riskReward':
      return idea.riskReward;
    case 'probability':
      return idea.bucket?.probability ?? null;
    case 'expectancy':
      return idea.bucket?.expectancy ?? null;
    default:
      return idea.bucket === null ? null : String(idea.bucket.cases);
  }
}

/** A pair of the market meets the filter; the conditions on idea fields act only on «Идеи» and are skipped here. */
export function matchesMarketRow(filter: SavedFilter, row: MarketRow): boolean {
  return filter.conditions.every(
    (condition) =>
      isIdeaField(condition.field) || holds(condition, marketValue(row, condition.field!)),
  );
}

/**
 * An idea meets the filter: its own fields from the idea and its 90-day bucket (the ▲ rule for «выше базовой»), market
 * fields from the row of its pair, which an idea without a row cannot meet.
 */
export function matchesIdea(filter: SavedFilter, idea: Idea, row: MarketRow | undefined): boolean {
  return filter.conditions.every((condition) => {
    const field = condition.field!;

    if (field === 'aboveBaseline') {
      return probabilityView(idea.bucket, idea.baseline).badge === '▲';
    }

    if (isIdeaField(field)) {
      return holds(condition, ideaValue(idea, field));
    }

    return row !== undefined && holds(condition, marketValue(row, field));
  });
}
