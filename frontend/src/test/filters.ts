import type { FilterField, SavedFilter } from '@/api/filters';

import { type FakeRoute, json } from './fakeApi';

/** The fields of a filter as the API lists them (a part of them). */
export const filterFields: FilterField[] = [
  {
    name: 'change24h',
    title: 'Изменение 24 ч',
    type: 'number',
    place: 'market',
    unit: '%',
    values: null,
  },
  {
    name: 'turnover24h',
    title: 'Оборот 24 ч',
    type: 'number',
    place: 'market',
    unit: 'млн USDT',
    values: null,
  },
  {
    name: 'fundingRate',
    title: 'Фандинг',
    type: 'number',
    place: 'market',
    unit: '%',
    values: null,
  },
  { name: 'strategy', title: 'Стратегия', type: 'text', place: 'idea', unit: null, values: null },
  {
    name: 'timeframe',
    title: 'Таймфрейм',
    type: 'choice',
    place: 'idea',
    unit: null,
    values: ['5m', '15m', '1h', '4h', '1d'],
  },
  {
    name: 'side',
    title: 'Сторона',
    type: 'choice',
    place: 'idea',
    unit: null,
    values: ['long', 'short'],
  },
];

/** «Высокий фандинг» of «Рынок»: funding at least 0.03 % and turnover above 100 million. */
export const highFunding: SavedFilter = {
  id: 'f-high',
  name: 'Высокий фандинг',
  place: 'market',
  conditions: [
    { field: 'fundingRate', operator: 'ge', value: '0.03', to: null },
    { field: 'turnover24h', operator: 'gt', value: '100', to: null },
  ],
};

/** «Шорты» of «Идеи». */
export const shorts: SavedFilter = {
  id: 'f-shorts',
  name: 'Шорты',
  place: 'ideas',
  conditions: [{ field: 'side', operator: 'eq', value: 'short', to: null }],
};

/** The routes of the saved filters over a list kept in memory. */
export function filterRoutes(saved: SavedFilter[]): Record<string, FakeRoute> {
  return {
    'GET /api/filters': () => json(saved),
    'GET /api/filters/fields': () => json(filterFields),
  };
}
