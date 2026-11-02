import { queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';
import type { JsonNumber } from '@/shared/api/jsonNumber';

export const botsQuery = queryOptions({
  queryKey: ['bots'],
  queryFn: async () => unwrap(await api.GET('/api/bots')),
});

export function botQuery(id: string) {
  return queryOptions({
    queryKey: ['bots', id],
    queryFn: async () => unwrap(await api.GET('/api/bots/{id}', { params: { path: { id } } })),
  });
}

/** What the form sends: parameter values by name, the balance as the text typed, which the API reads as a decimal. */
export interface BotInput {
  readonly name: string;
  readonly strategyId?: string;
  readonly parameterValues: Readonly<Record<string, JsonNumber | string>>;
  readonly category: string;
  readonly symbol: string;
  readonly interval: string;
  readonly balance: string;
}

function bodyOf(input: BotInput): Schemas['BotBody'] {
  return {
    ...input,
    parameterValues: input.parameterValues as unknown as Schemas['JsonElement'],
    balance: input.balance.trim(),
  };
}

export async function createBot(input: BotInput): Promise<string> {
  return unwrap(await api.POST('/api/bots', { body: bodyOf(input) })).id;
}

export async function updateBot(id: string, input: BotInput): Promise<void> {
  unwrap(await api.PUT('/api/bots/{id}', { params: { path: { id } }, body: bodyOf(input) }));
}

export async function deleteBot(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/bots/{id}', { params: { path: { id } } }));
}
