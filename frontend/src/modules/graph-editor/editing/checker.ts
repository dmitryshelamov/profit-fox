const delay = 400;

/**
 * Checks an edit on the server once edits pause. A newer edit cancels the waiting check and aborts the one in flight,
 * and only the answer about the latest edit is applied: answers can arrive out of order. A check of the latest edit
 * that fails reports the failure, so the editor never waits for an answer that will not come.
 */
export function createChecker<E, R>(
  run: (edit: E, signal: AbortSignal) => Promise<R>,
  onResult: (result: R) => void,
  onError: (error: unknown) => void,
) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: AbortController | undefined;
  let latest = 0;

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
              onResult(result);
            }
          },
          (error: unknown) => {
            if (number === latest) {
              onError(error);
            }
          },
        );
      }, delay);
    },
    dispose() {
      clearTimeout(timer);
      inFlight?.abort();
    },
  };
}
