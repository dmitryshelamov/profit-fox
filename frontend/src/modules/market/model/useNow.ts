import { useEffect, useState } from 'react';

/** The current time in milliseconds, renewed every second, so an age on the screen keeps growing. */
export function useNow(): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => clearInterval(timer);
  }, []);

  return now;
}
