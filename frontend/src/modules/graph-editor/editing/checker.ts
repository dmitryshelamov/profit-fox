const delay = 400;

/**
 * Checks an edit on the server once edits pause. A newer edit cancels the waiting check and aborts the one in flight,
 * and only the answer about the latest edit is applied: answers can arrive out of order.
 */
export function createChecker<E, R>(
  run: (edit: E, signal: AbortSignal) => Promise<R>,
  onResult: (result: R) => void,
) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: AbortController | undefined;
  let latest = 0;
  let answered = 0;

  return {
    request(edit: E) {
      const number = ++latest;
      clearTimeout(timer);
      inFlight?.abort();

      timer = setTimeout(() => {
        const controller = new AbortController();
        inFlight = controller;

        void run(edit, controller.signal).then(
          (result) => {
            if (number === latest) {
              answered = number;
              onResult(result);
            }
          },
          () => undefined,
        );
      }, delay);
    },
    /** Whether the latest edit has no answer yet. */
    pending() {
      return answered !== latest;
    },
    dispose() {
      clearTimeout(timer);
      inFlight?.abort();
    },
  };
}
