import { useEffect, useState } from 'react';

import type { InstrumentKind } from '@/api/market';

/** The words of the kinds above the tables and on the page of a pair. */
export const kindWords: Readonly<
  Record<InstrumentKind, { readonly many: string; readonly one: string }>
> = {
  crypto: { many: 'крипта', one: 'крипта' },
  stock: { many: 'акции', one: 'акция' },
};

/** Where the browser keeps the kind chosen: one key for «Рынок», «Открытые» and «Журнал» (decision 5). */
export const instrumentKindKey = 'profit-fox.instrument-kind';

/** The event that tells the other tables of this tab the kind chosen; the event `storage` tells other tabs. */
const chosenEvent = 'profit-fox:instrument-kind';

export function isInstrumentKind(value: unknown): value is InstrumentKind {
  return value === 'crypto' || value === 'stock';
}

function readKind(): InstrumentKind | undefined {
  try {
    const value = localStorage.getItem(instrumentKindKey);
    return isInstrumentKind(value) ? value : undefined;
  } catch {
    return undefined;
  }
}

function writeKind(kind: InstrumentKind | undefined): void {
  try {
    if (kind === undefined) {
      localStorage.removeItem(instrumentKindKey);
    } else {
      localStorage.setItem(instrumentKindKey, kind);
    }
  } catch {
    // A browser without storage remembers nothing; the choice still holds on the page.
  }
}

/**
 * The kind of a pair chosen above the tables of «Рынок» and «Идеи»: every kind (`undefined`), `crypto` or `stock`. One
 * value for the three tables, kept in the browser; a browser that keeps nothing starts with every kind (spec
 * market-overview «Браузер не помнит»). A choice in another tab comes by the event `storage`.
 */
export function useInstrumentKind() {
  const [kind, setKind] = useState<InstrumentKind | undefined>(readKind);
  useEffect(() => {
    const fromTab = (event: Event) => {
      const chosen = (event as CustomEvent<InstrumentKind | undefined>).detail;
      setKind(isInstrumentKind(chosen) ? chosen : undefined);
    };
    const fromStorage = (event: StorageEvent) => {
      if (event.key === instrumentKindKey || event.key === null) {
        setKind(isInstrumentKind(event.newValue) ? event.newValue : undefined);
      }
    };
    window.addEventListener(chosenEvent, fromTab);
    window.addEventListener('storage', fromStorage);
    return () => {
      window.removeEventListener(chosenEvent, fromTab);
      window.removeEventListener('storage', fromStorage);
    };
  }, []);

  return {
    kind,
    choose: (next: InstrumentKind | undefined) => {
      setKind(next);
      writeKind(next);
      window.dispatchEvent(new CustomEvent(chosenEvent, { detail: next }));
    },
  };
}
