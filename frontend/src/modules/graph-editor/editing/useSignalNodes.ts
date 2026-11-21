import { useQuery } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState } from 'react';

import { listSignals } from '@/api/signals';

import {
  mergeUses,
  type SignalEntry,
  type SignalUse,
  versionKey,
  withSignalVersions,
} from '../model/signals';
import type { Catalog, NodeKind, SignalVersionInfo } from '../model/types';

/**
 * What the editor of a strategy knows of the signals its nodes point at (change add-strategy-constructor, design.md,
 * decision 7): the uses the read of the strategy and every check bring, the saved signals of the palette — whose latest
 * versions give a node its name before its pins come — and versions the page reads itself, such as the target of an
 * update. Off for a signal, which has no signal nodes.
 */
export function useSignalNodes(
  enabled: boolean,
  catalog: readonly NodeKind[],
  initial: readonly SignalUse[],
  checked: readonly SignalUse[],
): {
  readonly catalog: Catalog;
  readonly palette: readonly SignalEntry[];
  readonly learn: (signal: string, version: number, info: SignalVersionInfo) => void;
} {
  const [versions, setVersions] = useState(() => mergeUses(new Map(), initial));
  useEffect(() => {
    if (checked.length > 0) {
      setVersions((current) => mergeUses(current, checked));
    }
  }, [checked]);

  const signals = useQuery({ queryKey: ['signals'], queryFn: listSignals, enabled });
  const palette = useMemo(
    (): SignalEntry[] =>
      (signals.data ?? [])
        .filter((signal) => !signal.error && signal.id)
        .map((signal) => ({ id: signal.id!, name: signal.name, version: Number(signal.version) })),
    [signals.data],
  );

  const known = useMemo(() => {
    const all = new Map<string, SignalVersionInfo>();
    for (const entry of palette) {
      all.set(versionKey(entry.id, entry.version), {
        name: entry.name,
        latestVersion: entry.version,
      });
    }

    for (const [key, info] of versions) {
      all.set(key, info);
    }

    return all;
  }, [palette, versions]);

  const learn = useCallback((signal: string, version: number, info: SignalVersionInfo) => {
    setVersions((current) => new Map(current).set(versionKey(signal, version), info));
  }, []);

  return {
    catalog: useMemo(
      () => (enabled ? withSignalVersions(catalog, known) : catalog),
      [enabled, catalog, known],
    ),
    palette,
    learn,
  };
}
