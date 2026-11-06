import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import type { StrategyDef } from '@/modules/graph-editor';
import { draftKey, drafts } from '@/modules/graph-editor';
import {
  directionTitles,
  fetchStrategyTemplate,
  strategyTemplatesQuery,
} from '@/modules/strategies';
import { newDraftId } from '@/shared/id/newDraftId';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

const card =
  'flex w-full flex-col items-start gap-1 rounded-lg border border-line bg-panel-2 p-3 text-left hover:border-accent disabled:cursor-wait disabled:opacity-60';

/** The choice a new strategy starts from: an empty canvas or a built-in template. Either way it is a draft in the browser until it is saved. */
export function NewStrategyDialog({ onClose }: { onClose: () => void }) {
  const templates = useQuery(strategyTemplatesQuery);
  const navigate = useNavigate();
  const [opening, setOpening] = useState(false);

  async function start(code?: string) {
    const draftId = newDraftId();

    if (code) {
      setOpening(true);
      try {
        const template = await fetchStrategyTemplate(code);
        drafts.save(
          draftKey({ newStrategy: draftId }),
          { name: template.name, definition: template.definition as unknown as StrategyDef },
          new Date(),
        );
      } catch {
        setOpening(false);

        return;
      }
    }

    void navigate({ to: '/strategies/new/$draftId', params: { draftId } });
  }

  return (
    <Dialog
      title="Новая стратегия"
      width={760}
      onClose={onClose}
      actions={<Button onClick={onClose}>Отмена</Button>}
    >
      <button
        type="button"
        aria-label="Пустая стратегия"
        className={card}
        disabled={opening}
        onClick={() => void start()}
      >
        <span className="font-semibold">Пустая стратегия</span>
        <span className="text-xs text-muted">Чистый холст с узлом «Параметры».</span>
      </button>
      {templates.isError && <p role="alert">Шаблоны не загрузились.</p>}
      {templates.data && (
        <div className="grid max-h-[55vh] grid-cols-2 gap-2.5 overflow-y-auto">
          {templates.data.map((template) => (
            <button
              key={template.code}
              type="button"
              className={card}
              disabled={opening}
              onClick={() => void start(template.code)}
            >
              <span className="flex w-full items-center justify-between gap-2">
                <span className="font-semibold">{template.name}</span>
                <Badge>{directionTitles[template.direction] ?? template.direction}</Badge>
              </span>
              <span className="text-xs text-muted">{template.description}</span>
            </button>
          ))}
        </div>
      )}
    </Dialog>
  );
}
