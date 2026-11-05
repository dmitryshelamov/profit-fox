import type { Schemas } from '@/shared/api/client';

export type MarketRow = Schemas['MarketRowResponse'];

export type CategoryFilter = 'all' | 'linear' | 'spot';
