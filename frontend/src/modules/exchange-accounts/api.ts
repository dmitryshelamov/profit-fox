import { queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

export type ExchangeAccount = Schemas['ExchangeAccountResponse'];
export type BrokerAccount = Schemas['BrokerAccountResponse'];

/** The name of an exchange on the screen. */
export function exchangeName(exchange: string): string {
  return exchange === 'tinvest' ? 'T-Invest' : 'Bybit';
}

export const accountsQuery = queryOptions({
  queryKey: ['exchange-accounts'],
  queryFn: async () => unwrap(await api.GET('/api/exchange-accounts')),
});

export function accountQuery(id: string) {
  return queryOptions({
    queryKey: ['exchange-accounts', id],
    queryFn: async () =>
      unwrap(await api.GET('/api/exchange-accounts/{id}', { params: { path: { id } } })),
  });
}

/** Read from Bybit on demand only: the query stays disabled until the page asks for it. */
export function accountBalanceQuery(id: string) {
  return queryOptions({
    queryKey: ['exchange-accounts', id, 'balance'],
    queryFn: async () =>
      unwrap(await api.GET('/api/exchange-accounts/{id}/balance', { params: { path: { id } } })),
    enabled: false,
    retry: false,
  });
}

export async function createAccount(body: Schemas['ExchangeAccountBody']): Promise<string> {
  return unwrap(await api.POST('/api/exchange-accounts', { body })).id;
}

/** The accounts a T-Invest token opens, each with whether it can be added; the token is stored nowhere. */
export async function listBrokerAccounts(token: string): Promise<BrokerAccount[]> {
  return unwrap(await api.POST('/api/exchange-accounts/tinvest/accounts', { body: { token } }));
}

export async function renameAccount(id: string, name: string): Promise<void> {
  unwrap(
    await api.PUT('/api/exchange-accounts/{id}', { params: { path: { id } }, body: { name } }),
  );
}

export async function deleteAccount(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/exchange-accounts/{id}', { params: { path: { id } } }));
}
