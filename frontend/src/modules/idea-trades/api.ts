import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';
import { isDecimalText, jsonNumberOf } from '@/shared/api/jsonNumber';

/** The number as typed, with its digits, for a field of a body that is a number; the type of the generated body wants a `number`. */
const exact = (text: string) => jsonNumberOf(text) as unknown as number;

const given = (text: string) => (text === '' ? undefined : text);

/** What the journal is filtered by; an empty text means no limit. */
export interface JournalFilters {
  readonly setup: string;
  readonly symbol: string;
  readonly side: string;
  /** `up`, `flat`, `down`, or `none` for a trade with no badge. */
  readonly badge: string;
  /** The period of the time of the close in days: 7, 30 or 90. */
  readonly days: string;
}

export const noJournalFilters: JournalFilters = {
  setup: '',
  symbol: '',
  side: '',
  badge: '',
  days: '',
};

/** What the journal is asked for: the filters, and the number and the size of the page of the closed trades. */
export interface JournalRequest {
  readonly filters: JournalFilters;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * The journal of the trades by ideas: the open trades, a page of the closed ones and the totals of them all, read again every 15
 * seconds; the API filters, cuts and counts. The old page stays on the screen while a new one loads.
 */
export function ideaJournalQuery(request: JournalRequest) {
  const { filters, page, pageSize } = request;

  return queryOptions({
    queryKey: ['idea-trades', 'journal', request],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/idea-trades', {
          params: {
            query: {
              Setup: given(filters.setup),
              Symbol: given(filters.symbol),
              Side: given(filters.side),
              Badge: given(filters.badge),
              Days: given(filters.days) === undefined ? undefined : Number(filters.days),
              Page: page,
              PageSize: pageSize,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  });
}

/** The settings of trading by ideas. */
export function tradingSettingsQuery() {
  return queryOptions({
    queryKey: ['idea-trades', 'settings'],
    queryFn: async () => unwrap(await api.GET('/api/idea-trading/settings')),
  });
}

/**
 * The entry of an idea as it would be made now, for the account and the risk typed; left empty they come from the settings. Asking is
 * a calculation and places nothing, so it is a query, read again every 5 seconds while the window that shows it is open.
 */
export function ideaEntryQuery(ideaId: string, accountId: string | null, risk: string) {
  return queryOptions({
    queryKey: ['idea-trades', 'entry', ideaId, accountId, risk],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/ideas/{id}/entry', {
          params: {
            path: { id: ideaId },
            query: { accountId: accountId ?? undefined, risk: given(risk) },
          },
        }),
      ),
    refetchInterval: 5_000,
    retry: false,
  });
}

export interface EntryInput {
  readonly ideaId: string;
  readonly accountId: string | null;
  readonly risk: string;
}

/** Enters by an idea and gives the id of the trade. The account and the risk left out come from the settings. */
export async function enterIdea(input: EntryInput): Promise<string> {
  const { id } = unwrap(
    await api.POST('/api/idea-trades', {
      body: {
        ideaId: input.ideaId,
        ...(input.accountId === null ? {} : { accountId: input.accountId }),
        ...(isDecimalText(input.risk) ? { risk: exact(input.risk) } : {}),
      },
    }),
  );

  return id;
}

export async function cancelTrade(id: string): Promise<void> {
  unwrap(await api.POST('/api/idea-trades/{id}/cancel', { params: { path: { id } } }));
}

export async function closeTrade(id: string): Promise<void> {
  unwrap(await api.POST('/api/idea-trades/{id}/close', { params: { path: { id } } }));
}

/** The settings as the form holds them, every number as typed. */
export interface SettingsInput {
  readonly accountId: string | null;
  readonly risk: string;
  readonly maxAmount: string;
  readonly maxOpenTrades: string;
  readonly maxTotalRisk: string;
  readonly minRiskReward: string;
}

/** A number the form holds as text; text that is no number goes out as it is, so the API names the field. */
const field = (text: string) => (isDecimalText(text) ? exact(text) : text);

export async function saveSettings(input: SettingsInput): Promise<void> {
  unwrap(
    await api.PUT('/api/idea-trading/settings', {
      body: {
        accountId: input.accountId,
        risk: field(input.risk),
        maxAmount: field(input.maxAmount),
        maxOpenTrades: field(input.maxOpenTrades),
        maxTotalRisk: field(input.maxTotalRisk),
        minRiskReward: field(input.minRiskReward),
      },
    }),
  );
}

/** Enters by an idea; the list of trades is read again afterwards. */
export function useEnterIdea() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: enterIdea,
    onSuccess: () => client.invalidateQueries({ queryKey: ['idea-trades'] }),
  });
}

export function useCancelTrade() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: cancelTrade,
    onSuccess: () => client.invalidateQueries({ queryKey: ['idea-trades', 'journal'] }),
  });
}

export function useCloseTrade() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: closeTrade,
    onSuccess: () => client.invalidateQueries({ queryKey: ['idea-trades', 'journal'] }),
  });
}

export function useSaveSettings() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: saveSettings,
    onSuccess: () => client.invalidateQueries({ queryKey: ['idea-trades'] }),
  });
}
