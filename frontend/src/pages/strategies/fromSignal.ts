import type { useNavigate } from '@tanstack/react-router';

import { strategyDraftKey, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { type SignalEntry, strategyOfSignal } from '@/modules/graph-editor/model/signals';

/**
 * «Создать стратегию» of a saved signal: writes the draft of a new strategy of ideas with the signal as its node and
 * opens it (spec strategy-constructor «Стратегия из сигнала»). The signal does not change, and the strategy is not in
 * the list before its first save.
 */
export async function openStrategyOfSignal(
  navigate: ReturnType<typeof useNavigate>,
  signal: SignalEntry,
): Promise<void> {
  const draftId = crypto.randomUUID();
  writeDraft(strategyDraftKey({ newId: draftId }), {
    description: '',
    ...strategyOfSignal(signal),
    kind: 'idea',
  });
  await navigate({ to: '/strategies/new/$draftId', params: { draftId } });
}
