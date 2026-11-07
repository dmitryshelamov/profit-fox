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
  /** Who took the trade: `manual`, `auto` (with money) or `paper`. */
  readonly origin: string;
  /** The id of one auto, real and paper trades alike. */
  readonly autoId: string;
}

export const noJournalFilters: JournalFilters = {
  setup: '',
  symbol: '',
  side: '',
  badge: '',
  days: '',
  origin: '',
  autoId: '',
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
              Origin: given(filters.origin),
              AutoId: given(filters.autoId),
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
 * The entry of an idea as it would be made now, for the account and the amount typed; left empty they come from the settings. Asking is
 * a calculation and places nothing, so it is a query, read again every 5 seconds while the window that shows it is open.
 */
export function ideaEntryQuery(ideaId: string, accountId: string | null, amount: string) {
  return queryOptions({
    queryKey: ['idea-trades', 'entry', ideaId, accountId, amount],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/ideas/{id}/entry', {
          params: {
            path: { id: ideaId },
            query: { accountId: accountId ?? undefined, amount: given(amount) },
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
  readonly amount: string;
}

/** Enters by an idea and gives the id of the trade. The account left out comes from the settings. */
export async function enterIdea(input: EntryInput): Promise<string> {
  const { id } = unwrap(
    await api.POST('/api/idea-trades', {
      body: {
        ideaId: input.ideaId,
        ...(input.accountId === null ? {} : { accountId: input.accountId }),
        amount: field(input.amount),
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
  readonly amount: string;
  readonly maxAmount: string;
  readonly maxOpenTrades: string;
  readonly maxTotalRisk: string;
  readonly minRiskReward: string;
  /** The loss of a day that stops the autos with money; an empty text switches the guard off. */
  readonly dailyLossLimit: string;
  /** The most open trades of one side that the autos with money stand; an empty text switches the guard off. */
  readonly maxSameDirection: string;
  /** The most the open real trades of all the autos may hold; an empty text switches the guard off. */
  readonly autosBudget: string;
}

/** A number the form holds as text; text that is no number goes out as it is, so the API names the field. */
const field = (text: string) => (isDecimalText(text) ? exact(text) : text);

export async function saveSettings(input: SettingsInput): Promise<void> {
  unwrap(
    await api.PUT('/api/idea-trading/settings', {
      body: {
        accountId: input.accountId,
        amount: field(input.amount),
        maxAmount: field(input.maxAmount),
        maxOpenTrades: field(input.maxOpenTrades),
        maxTotalRisk: field(input.maxTotalRisk),
        minRiskReward: field(input.minRiskReward),
        dailyLossLimit: input.dailyLossLimit === '' ? null : field(input.dailyLossLimit),
        maxSameDirection: input.maxSameDirection === '' ? null : field(input.maxSameDirection),
        autosBudget: input.autosBudget === '' ? null : field(input.autosBudget),
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
