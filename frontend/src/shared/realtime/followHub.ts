import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

/** What a page follows on a hub: the method it joins with and its arguments, the messages it handles and the refusal no retry changes. */
export interface HubFollow<TSnapshot> {
  readonly url: string;
  readonly method: string;
  readonly args: readonly unknown[];
  /** The hub refuses with this message what it does not know; no retry can change that. */
  readonly refusal: string;
  /** The handler of each message the hub sends, by its name. */
  readonly messages: Readonly<Record<string, (message: never) => void>>;
  /** `superseded`: a message came after the join was invoked, so the snapshot is older than it. */
  readonly onSnapshot: (snapshot: TSnapshot, superseded: boolean) => void;
  /** The link to the server is lost or being restored, or the join failed: nothing is arriving. */
  readonly onLost: () => void;
}

/** Seconds before each reconnection attempt: 0, 2, 10, 30, and then 30 for as long as the page is open. */
const retryDelays = [0, 2000, 10_000, 30_000];

const retryDelay = (count: number) => retryDelays[Math.min(count, retryDelays.length - 1)]!;

/**
 * Follows a hub of the API. Joining answers with the state of the feed and its latest message; after every reconnection it joins again. The
 * connection never gives up: it reconnects with a capped delay, and a failed start or a rejected join is tried again after a pause, except for
 * the refusal of the hub. Leaving the page stops the connection, which lets the server drop the feed with its last follower. Returns the
 * function that stops following.
 */
export function followHub<TSnapshot>(follow: HubFollow<TSnapshot>): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl(follow.url)
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
      follow.onLost();
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
      .invoke<TSnapshot>(follow.method, ...follow.args)
      .then((snapshot) => {
        failures = 0;

        if (!stopped) {
          follow.onSnapshot(snapshot, messages !== seen);
        }
      })
      .catch((error: unknown) => {
        lost();

        if (!stopped && !String(error).includes(follow.refusal)) {
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

  for (const [name, handle] of Object.entries(follow.messages)) {
    connection.on(name, (message: never) => {
      messages += 1;

      if (!stopped) {
        handle(message);
      }
    });
  }
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
