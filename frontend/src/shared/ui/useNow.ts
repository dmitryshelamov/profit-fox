import { useEffect, useState } from 'react';

/**
 * The clock of the browser, read again every `periodMs`: what depends on the time alone — a countdown, a snapshot that
 * gets old (task 18.19) — changes on an open page without a new answer of the server.
 */
export function useNow(periodMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), periodMs);
    return () => clearInterval(timer);
  }, [periodMs]);
  return now;
}
