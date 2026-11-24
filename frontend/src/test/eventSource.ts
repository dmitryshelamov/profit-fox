/**
 * The streams of the page in the tests: jsdom has no `EventSource`, so each stream the page opens is one of these, and
 * a test sends its events or breaks it.
 */
export class FakeEventSource {
  static readonly opened: FakeEventSource[] = [];

  readonly listeners = new Map<string, ((event: MessageEvent<string>) => void)[]>();
  closed = false;

  constructor(readonly url: string) {
    FakeEventSource.opened.push(this);
  }

  /** The stream opened last. */
  static last(): FakeEventSource {
    const source = FakeEventSource.opened.at(-1);
    if (source === undefined) {
      throw new Error('The page opened no stream.');
    }

    return source;
  }

  static reset(): void {
    FakeEventSource.opened.length = 0;
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close(): void {
    this.closed = true;
  }

  /** An event of the stream with its data as JSON. */
  send(type: string, data: unknown): void {
    const event = new MessageEvent<string>(type, { data: JSON.stringify(data) });
    (this.listeners.get(type) ?? []).forEach((listener) => listener(event));
  }

  /** The stream broke. */
  fail(): void {
    (this.listeners.get('error') ?? []).forEach((listener) =>
      listener(new MessageEvent<string>('error')),
    );
  }
}
