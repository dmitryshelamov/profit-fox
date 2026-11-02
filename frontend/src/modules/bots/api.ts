import { queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';
import type { JsonNumber } from '@/shared/api/jsonNumber';

export const botsQuery = queryOptions({
  queryKey: ['bots'],
  queryFn: async () => unwrap(await api.GET('/api/bots')),
});

/** Whether a bot trades or waits for its cancels: it cannot be changed then. */
export function isWorking(status: string | undefined): boolean {
  return status === 'running' || status === 'paused' || status === 'stopping';
}

/** Whether a Worker keeps the bot, also to close its position: its page and the list then follow it every 5 seconds. */
export function isFollowed(bot: { status: string; closeRequested: boolean } | undefined): boolean {
  return bot !== undefined && (isWorking(bot.status) || bot.closeRequested);
}

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
  /** The exchange account the bot will trade with; `null` for none. */
  readonly exchangeAccountId: string | null;
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

export function botPositionsQuery(id: string) {
  return queryOptions({
    queryKey: ['bots', id, 'positions'],
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/positions', { params: { path: { id } } })),
  });
}

export type BotAction = 'start' | 'pause' | 'resume' | 'stop' | 'close-position';

/** An action of live trading; a refusal comes as an `ApiError` with the reason of the server. */
export async function actOnBot(id: string, action: BotAction): Promise<void> {
  const options = { params: { path: { id } } };

  switch (action) {
    case 'start':
      unwrap(await api.POST('/api/bots/{id}/start', options));
      break;
    case 'pause':
      unwrap(await api.POST('/api/bots/{id}/pause', options));
      break;
    case 'resume':
      unwrap(await api.POST('/api/bots/{id}/resume', options));
      break;
    case 'stop':
      unwrap(await api.POST('/api/bots/{id}/stop', options));
      break;
    case 'close-position':
      unwrap(await api.POST('/api/bots/{id}/close-position', options));
      break;
  }
}
