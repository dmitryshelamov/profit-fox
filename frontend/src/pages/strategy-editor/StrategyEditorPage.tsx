import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useCallback, useState } from 'react';

import {
  BarDivider,
  type CatalogNode,
  CheckFailedNotice,
  draftKey,
  DraftOfferDialog,
  drafts,
  type Edited,
  FloatingBar,
  GraphCanvas,
  IssuesCard,
  LeaveGuard,
  NodeCard,
  RejectionView,
  StrategyCard,
  type StrategyDef,
  useGraphEditing,
} from '@/modules/graph-editor';
import { catalogQuery } from '@/modules/nodes';
import {
  createStrategy,
  directionTitles,
  saveStrategy,
  strategiesQuery,
  strategyQuery,
  validateStrategy,
} from '@/modules/strategies';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

export function StrategyEditorPage({ id }: { id: string }) {
  const strategy = useQuery(strategyQuery(id));
  const catalog = useQuery(catalogQuery);

  if (strategy.isError) {
    return (
      <div className="space-y-3 p-6">
        <p className="text-muted">Стратегия не найдена.</p>
        <Link to="/strategies" className="text-accent hover:underline">
          К стратегиям
        </Link>
      </div>
    );
  }

  if (!strategy.data || !catalog.data) {
    return <p className="p-6 text-muted">Загрузка…</p>;
  }

  return (
    <StrategyEditor
      id={id}
      draft={draftKey({ strategy: id })}
      saved={{
        name: strategy.data.name,
        definition: strategy.data.definition as unknown as StrategyDef,
      }}
      catalog={catalog.data as CatalogNode[]}
    />
  );
}

const template: Edited<StrategyDef> = {
  name: '',
  definition: {
    formatVersion: 1,
    direction: 'long',
    parameters: [],
    nodes: [{ id: 'params', kind: 'parameters' }],
    wires: [],
    layout: { params: { x: 0, y: 0 } },
  },
};

/** A strategy the server does not have yet: it lives as a draft until its first save creates it. */
export function NewStrategyEditorPage({ draftId }: { draftId: string }) {
  const catalog = useQuery(catalogQuery);
  const [draft] = useState(() => drafts.read(draftKey({ newStrategy: draftId })));

  if (!catalog.data) {
    return <p className="p-6 text-muted">Загрузка…</p>;
  }

  return (
    <StrategyEditor
      key={draftId}
      id={undefined}
      saved={template}
      initial={draft && { name: draft.name, definition: draft.definition as StrategyDef }}
      draft={draftKey({ newStrategy: draftId })}
      catalog={catalog.data as CatalogNode[]}
    />
  );
}

type Panel = 'strategy' | 'issues' | undefined;

function StrategyEditor({
  id,
  saved,
  initial,
  draft,
  catalog,
}: {
  /** No id: a new strategy, created by its first save. */
  id: string | undefined;
  saved: Edited<StrategyDef>;
  initial?: Edited<StrategyDef>;
  draft: string;
  catalog: CatalogNode[];
}) {
  const validate = useCallback(
    (edited: Edited<StrategyDef>, signal: AbortSignal) => validateStrategy(id, edited, signal),
    [id],
  );
  const editing = useGraphEditing({ saved, initial, validate, draftKey: draft, catalog });
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  async function save() {
    let created: string | undefined;

    await editing.save(async (edited) => {
      if (id) {
        await saveStrategy(id, edited);
      } else {
        created = await createStrategy(edited);
      }

      await queryClient.invalidateQueries({ queryKey: strategiesQuery.queryKey });
    });

    if (created) {
      // Saved: nothing is left to lose, though the editor has not re-rendered as clean yet.
      await navigate({
        to: '/strategies/$id',
        params: { id: created },
        replace: true,
        ignoreBlocker: true,
      });
    }
  }
  const { name, definition } = editing;
  const [selected, setSelected] = useState<string>();
  const [panel, setPanel] = useState<Panel>();

  function open(next: Panel) {
    setPanel(panel === next ? undefined : next);
    setSelected(undefined);
  }

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-canvas">
      <GraphCanvas
        definition={definition}
        catalog={catalog}
        issues={editing.issues}
        readOnly={false}
        onChange={editing.setDefinition}
        onRelayout={editing.relayout}
        onSelect={(nodeId) => {
          setSelected(nodeId);
          setPanel(undefined);
        }}
      />
      <FloatingBar label="Стратегия" side="left">
        <Link to="/strategies" className="rounded-md px-3 py-2 text-sm hover:bg-panel-2">
          ‹ Стратегии
        </Link>
        <BarDivider />
        <span className="px-2 text-sm font-bold">{name || 'Без названия'}</span>
        <Badge>{directionTitles[definition.direction]}</Badge>
      </FloatingBar>
      <FloatingBar label="Действия" side="right">
        <Button
          look="plain"
          aria-expanded={panel === 'issues'}
          className={panel === 'issues' ? 'bg-[#2d323d]' : ''}
          onClick={() => open('issues')}
        >
          Ошибки
          <span
            className={`grid h-[18px] min-w-5 place-items-center rounded-full px-1 text-[11px] font-bold text-white ${editing.errorCount > 0 ? 'bg-err' : 'bg-ok'}`}
          >
            {editing.errorCount}
          </span>
        </Button>
        <Button
          look="plain"
          aria-expanded={panel === 'strategy'}
          className={panel === 'strategy' ? 'bg-[#2d323d]' : ''}
          onClick={() => open('strategy')}
        >
          Параметры
        </Button>
        <BarDivider />
        <Button look="plain" disabled={!editing.dirty} onClick={editing.discard}>
          Отменить правки
        </Button>
        <Button look="primary" disabled={!editing.canSave} onClick={() => void save()}>
          Сохранить
        </Button>
      </FloatingBar>
      {selected && (
        <NodeCard
          definition={definition}
          catalog={catalog}
          nodeId={selected}
          readOnly={false}
          onChange={editing.setDefinition}
          onClose={() => setSelected(undefined)}
        />
      )}
      {panel === 'strategy' && (
        <StrategyCard
          name={name}
          definition={definition}
          catalog={catalog}
          readOnly={false}
          onName={editing.setName}
          onChange={editing.setDefinition}
          onClose={() => setPanel(undefined)}
        />
      )}
      <LeaveGuard dirty={editing.dirty} />
      {editing.offer && (
        <DraftOfferDialog
          offer={editing.offer}
          what={`стратегии «${saved.name}»`}
          onRestore={editing.restoreDraft}
          onDrop={editing.dropDraft}
        />
      )}
      {editing.checkFailed && <CheckFailedNotice onRetry={editing.recheck} />}
      {editing.rejection && (
        <RejectionView rejection={editing.rejection} onClose={editing.dismissRejection} />
      )}
      {panel === 'issues' && (
        <IssuesCard
          issues={editing.issues}
          report={editing.report}
          onShow={(nodeId) => {
            setPanel(undefined);
            setSelected(nodeId);
          }}
          onClose={() => setPanel(undefined)}
        />
      )}
    </div>
  );
}
