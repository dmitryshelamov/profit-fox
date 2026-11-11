import { followHub } from '@/shared/realtime/followHub';

import type { FormingCandle, FormingFeed, FormingSnapshot } from '../model/formingCandle';

export interface FormingHandlers {
  /** `superseded`: a candle or a state message came after `Subscribe` was invoked, so the snapshot is older than them. */
  onSnapshot: (snapshot: FormingSnapshot, superseded: boolean) => void;
  onCandle: (candle: FormingCandle) => void;
  onStatus: (status: FormingFeed) => void;
  /** The link to the server is lost or being restored, or the subscription failed: nothing is arriving. */
  onLost: () => void;
}

/**
 * Follows the current candle of a bot over `/api/hubs/bot-chart` (see `followHub`): `Subscribe` answers with the state of the feed and its latest
 * candle, then `FormingCandle` and `FormingStatus` come; an unknown bot is not tried again. Returns the function that stops following.
 */
export function followFormingCandle(botId: string, handlers: FormingHandlers): () => void {
  return followHub<FormingSnapshot>({
    url: '/api/hubs/bot-chart',
    method: 'Subscribe',
    args: [botId],
    refusal: 'bot_not_found',
    messages: { FormingCandle: handlers.onCandle, FormingStatus: handlers.onStatus },
    onSnapshot: handlers.onSnapshot,
    onLost: handlers.onLost,
  });
}
