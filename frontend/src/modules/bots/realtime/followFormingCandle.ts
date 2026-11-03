import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import type { FormingCandle, FormingFeed, FormingSnapshot } from '../model/formingCandle';

export interface FormingHandlers {
  /** `superseded`: a candle or a state message came after `Subscribe` was invoked, so the snapshot is older than them. */
  onSnapshot: (snapshot: FormingSnapshot, superseded: boolean) => void;
  onCandle: (candle: FormingCandle) => void;
  onStatus: (status: FormingFeed) => void;
  /** The link to the server is lost or being restored, or the subscription failed: nothing is arriving. */
  onLost: () => void;
}

/** Seconds before each reconnection attempt: 0, 2, 10, 30, and then 30 for as long as the page is open. */
const retryDelays = [0, 2000, 10_000, 30_000];

const retryDelay = (count: number) => retryDelays[Math.min(count, retryDelays.length - 1)]!;

/** The hub refuses a bot it does not know with this message; no retry can change that. */
const unknownBot = 'bot_not_found';

/**
 * Follows the current candle of a bot over `/api/hubs/bot-chart`. Subscribing answers with the state of the feed and its
 * latest candle; after every reconnection it subscribes again. The connection never gives up: it reconnects with a
 * capped delay, and a failed start or a rejected subscription is tried again after a pause, except for an unknown bot.
 * Leaving the page stops the connection, which lets the server drop the feed with its last follower. Returns the
 * function that stops following.
 */
export function followFormingCandle(botId: string, handlers: FormingHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/api/hubs/bot-chart')
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (context) => retryDelay(context.previousRetryCount),
    })
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;
  let started = false;
  let failures = 0;
  let messages = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const lost = () => {
    if (!stopped) {
      handlers.onLost();
    }
  };

  const retryLater = () => {
    failures += 1;
    clearTimeout(timer);
    timer = setTimeout(attempt, retryDelay(failures));
  };

  const subscribe = () => {
    const seen = messages;

    return connection
      .invoke<FormingSnapshot>('Subscribe', botId)
      .then((snapshot) => {
        failures = 0;

        if (!stopped) {
          handlers.onSnapshot(snapshot, messages !== seen);
        }
      })
      .catch((error: unknown) => {
        lost();

        if (!stopped && !String(error).includes(unknownBot)) {
          retryLater();
        }
      });
  };

  function attempt() {
    if (stopped) {
      return;
    }

    if (started) {
      void subscribe();
      return;
    }

    connection
      .start()
      .then(() => {
        started = true;

        return subscribe();
      })
      .catch(() => {
        lost();

        if (!stopped) {
          retryLater();
        }
      });
  }

  connection.on('FormingCandle', (candle: FormingCandle) => {
    messages += 1;

    if (!stopped) {
      handlers.onCandle(candle);
    }
  });
  connection.on('FormingStatus', (status: FormingFeed) => {
    messages += 1;

    if (!stopped) {
      handlers.onStatus(status);
    }
  });
  connection.onreconnecting(lost);
  connection.onclose(() => {
    lost();

    // The server closed the connection for good: automatic reconnection is over, so start it anew.
    if (!stopped) {
      started = false;
      retryLater();
    }
  });
  connection.onreconnected(() => {
    clearTimeout(timer);
    void subscribe();
  });
  attempt();

  return () => {
    stopped = true;
    clearTimeout(timer);
    void connection.stop();
  };
}
