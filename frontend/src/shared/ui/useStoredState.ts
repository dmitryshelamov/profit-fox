import { useState } from 'react';

import { readStored, writeStored } from '@/preview/stored';

/**
 * A value of the page kept in the browser under `key`, as the choices above a table: the page opens with what was chosen
 * last. `read` takes what the browser keeps and gives the value, or `undefined` for anything it cannot be (a value of an
 * older page, a broken one); then the page starts with `start`. A browser that keeps nothing starts with `start` too.
 */
export function useStoredState<T>(
  key: string,
  start: T,
  read: (stored: unknown) => T | undefined,
): readonly [T, (next: T) => void] {
  const [value, setValue] = useState<T>(() => read(readStored<unknown>(key, undefined)) ?? start);
  return [
    value,
    (next: T) => {
      setValue(next);
      writeStored(key, next);
    },
  ];
}
