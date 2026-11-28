import { api, type RequestError, type Schemas, unwrap } from './client';
import { type Written, written } from './signals';

/** An account of Bybit (change add-exchange-accounts, spec exchange-accounts «Список аккаунтов»); it has no secret. */
export type Account = Schemas['AccountResponse'];

/** The key of the list of accounts. */
export const accountsKey = ['accounts'] as const;

/** The accounts in the order they were added. */
export async function listAccounts(): Promise<Account[]> {
  return unwrap(await api.GET('/api/accounts'));
}

/**
 * Adds an account: the server asks Bybit what the key may do before it saves anything. A field, a key Bybit or the rules
 * refuse comes back as errors at `name`, `apiKey` or `secret`; Bybit that did not answer, or refused otherwise, throws.
 */
export async function addAccount(
  name: string,
  apiKey: string,
  secret: string,
): Promise<Written<Account>> {
  return written(await api.POST('/api/accounts', { body: { name, apiKey, secret } }));
}

/** Gives an account a new name; a taken or empty name comes back as an error at `name`. */
export async function renameAccount(id: string, name: string): Promise<Written<Account>> {
  return written(await api.PUT('/api/accounts/{id}', { params: { path: { id } }, body: { name } }));
}

/** Removes an account with its encrypted secret. */
export async function deleteAccount(id: string): Promise<void> {
  const result = await api.DELETE('/api/accounts/{id}', { params: { path: { id } } });
  if (!result.response.ok) {
    unwrap(result);
  }
}

/** The free money of an account read at Bybit now, or why Bybit or the bench could not tell it. */
export type Balance =
  | { readonly ok: true; readonly free: number; readonly marginMode: string | null }
  | { readonly ok: false; readonly reason: string };

/**
 * The money of the unified account free for a new position, in USDT, and its margin mode now (design.md, decisions 7 and
 * 11). A refusal is a value: a key
 * Bybit no longer takes (422), a secret that does not read (409), Bybit down (503) or another refusal of Bybit (502).
 */
export async function accountBalance(id: string): Promise<Balance> {
  const result = await api.GET('/api/accounts/{id}/balance', { params: { path: { id } } });
  if (result.response.ok && result.data) {
    return { ok: true, free: Number(result.data.free), marginMode: result.data.marginMode };
  }

  const error = (result.error ?? {}) as {
    errors?: RequestError[];
    detail?: string;
    title?: string;
  };
  const reason =
    error.errors?.map((problem) => problem.reason).join('; ') ??
    error.detail ??
    error.title ??
    result.response.statusText;
  return {
    ok: false,
    reason: result.response.status === 422 ? `Bybit отклоняет ключ: ${reason}` : reason,
  };
}

/** Free USDT with a space between the thousands and two digits after the point: `1 250.40`. */
export function freeText(free: number): string {
  const [whole = '0', fraction = '00'] = Math.abs(free).toFixed(2).split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${free < 0 ? '−' : ''}${grouped}.${fraction}`;
}

const marginModes: Readonly<Record<string, string>> = {
  ISOLATED_MARGIN: 'изолированная',
  REGULAR_MARGIN: 'кросс',
  PORTFOLIO_MARGIN: 'портфельная',
};

/**
 * The margin mode of an account in words (spec exchange-accounts «Режим маржи аккаунта»): «изолированная», «кросс»,
 * «портфельная»; a mode the bench does not know, or none, is «—».
 */
export function marginModeText(mode: string | null | undefined): string {
  return (mode && marginModes[mode]) ?? '—';
}
