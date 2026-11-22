import { useState } from 'react';

import { readStored, writeStored } from '@/preview/stored';

/** The place of a list with filters. */
export type ListPlace = 'market' | 'ideas';

/**
 * The filter chosen in a place, kept in the browser apart for each place (spec saved-filters «Строка фильтров»). A
 * filter deleted elsewhere is dropped when the list says so (`filterGone`).
 */
export function useChosenFilter(place: ListPlace) {
  const key = `profit-fox.filter.${place}`;
  const [chosen, setChosen] = useState<string | null>(() => readStored<string | null>(key, null));
  return {
    chosen,
    choose: (id: string | null) => {
      setChosen(id);
      writeStored(key, id);
    },
  };
}
