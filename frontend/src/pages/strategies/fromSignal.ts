import type { useNavigate } from '@tanstack/react-router';

import { strategyDraftKey, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { type SignalEntry, strategyOfSignal } from '@/modules/graph-editor/model/signals';

/**
 * «Создать стратегию» of a saved signal: writes the draft of a new strategy with the signal as its node and opens it.
 * The signal does not change, and the strategy is not in the list before its first save.
 */
export async function openStrategyOfSignal(
  navigate: ReturnType<typeof useNavigate>,
  signal: SignalEntry,
): Promise<void> {
  const draftId = crypto.randomUUID();
  writeDraft(strategyDraftKey({ newId: draftId }), {
    description: '',
    ...strategyOfSignal(signal),
  });
  await navigate({ to: '/strategies/new/$draftId', params: { draftId } });
}
