import { api, ApiError, type Schemas, unwrap } from './client';
import { pageOf, pageQuery, type PageRequest } from './lists';

/** A trade by an idea (change take-idea-trades, spec idea-trades «Раздел „Сделки“»). */
export type Trade = Schemas['TradeResponse'];
export type TradePage = Schemas['TradePageResponse'];
/** The count of an entry for the dialog (spec idea-trades «Расчёт перед входом»). */
export type EntryPreview = Schemas['EntryPreviewResponse'];
export type EntryCheck = Schemas['EntryCheckResponse'];

/** The key of everything about trades: the lists, the count of an entry. */
export const tradesKey = ['trades'] as const;

/** A page of the open trades — every state that holds its pair — or of the closed and cancelled ones. */
export async function listTrades(
  state: 'open' | 'closed',
  request: PageRequest,
): Promise<TradePage> {
  const query = { ...pageQuery(request), state };
  return pageOf(await api.GET('/api/trades', { params: { query } }));
}

/** What the entry asks for: the idea, and the account and the amount — empty ones from the settings of «Риск». */
export interface EntryAsk {
  readonly ideaId: string;
  readonly accountId?: string | null;
  readonly amount?: number | null;
}

/** The count of an entry with every check, read at Bybit now; Bybit down is an {@link ApiError} of 503. */
export async function previewEntry(ask: EntryAsk): Promise<EntryPreview> {
  return unwrap(
    await api.POST('/api/trades/preview', {
      body: { ideaId: ask.ideaId, accountId: ask.accountId ?? null, amount: ask.amount ?? null },
    }),
  );
}

/** The answer to an entry: the trade written, or the reasons of the refusal. */
export type Entered =
  | { readonly ok: true; readonly trade: Trade }
  | { readonly ok: false; readonly reasons: readonly string[] };

/**
 * Enters the idea. The server repeats every check; the checks that failed (422) and a refusal of the state or of Bybit come
 * back as reasons, so the dialog stays open and shows them.
 */
export async function openTrade(ask: EntryAsk): Promise<Entered> {
  const result = await api.POST('/api/trades', {
    body: { ideaId: ask.ideaId, accountId: ask.accountId ?? null, amount: ask.amount ?? null },
  });
  if (result.response.ok && result.data) {
    return { ok: true, trade: result.data };
  }

  return { ok: false, reasons: reasonsOf(result.error, result.response) };
}

/**
 * The answer to «Закрыть по рынку» or «Разобрана»: the trade, or why the server refused — and, for «Разобрана», whether the
 * owner may resolve without the check at Bybit (the key is not taken, the secret does not read).
 */
export type Acted =
  | { readonly ok: true; readonly trade: Trade }
  | { readonly ok: false; readonly reason: string; readonly canSkipCheck: boolean };

/** «Закрыть по рынку»: the close order is written, and the trading Worker sends it. */
export async function closeTrade(id: string): Promise<Acted> {
  return acted(await api.POST('/api/trades/{id}/close', { params: { path: { id } } }));
}

/**
 * «Разобрана»: a trade in «Ошибка» whose position is gone at Bybit is closed «на бирже». With `skipCheck` the owner confirms
 * the position on Bybit closed; the server takes it only when it cannot check (review R1).
 */
export async function resolveTrade(id: string, skipCheck = false): Promise<Acted> {
  return acted(
    await api.POST('/api/trades/{id}/resolve', {
      params: { path: { id }, query: skipCheck ? { unchecked: true } : {} },
    }),
  );
}

function acted(result: { data?: Trade; error?: unknown; response: Response }): Acted {
  if (result.response.ok && result.data) {
    return { ok: true, trade: result.data };
  }

  const body = (result.error ?? {}) as { canSkipCheck?: unknown };
  return {
    ok: false,
    reason: reasonsOf(result.error, result.response).join('; '),
    canSkipCheck: body.canSkipCheck === true,
  };
}

function reasonsOf(error: unknown, response: Response): string[] {
  const body = (error ?? {}) as {
    checks?: EntryCheck[];
    errors?: { reason: string }[];
    detail?: string;
    title?: string;
  };
  if (Array.isArray(body.checks)) {
    return body.checks.map((check) => check.text);
  }

  if (Array.isArray(body.errors)) {
    return body.errors.map((problem) => problem.reason);
  }

  return [body.detail ?? body.title ?? new ApiError(response.status, response.statusText).message];
}

/** The state of a trade in words (spec idea-trades «Состояния сделки»). */
export const tradeStates: Readonly<Record<string, string>> = {
  pending: 'Ждёт входа',
  open: 'Открыта',
  closing: 'Закрывается',
  closed: 'Закрыта',
  cancelled: 'Отменена',
  error: 'Ошибка',
};

/** The reason of a close in words (spec idea-trades «Причина закрытия»). */
export const closeReasons: Readonly<Record<string, string>> = {
  target: 'по цели',
  stop: 'по стопу',
  time: 'по сроку',
  manual: 'вручную',
  halt: 'стоп-кран',
  'no-stop': 'без стопа',
  liquidation: 'ликвидирована',
  adl: 'закрыта биржей (ADL)',
  exchange: 'на бирже',
};
