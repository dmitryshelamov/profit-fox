import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useCallback, useState } from 'react';

import {
  BarDivider,
  type CatalogNode,
  type CompositeDef,
  DeclarationsCard,
  draftKey,
  DraftOfferDialog,
  type Edited,
  FloatingBar,
  GraphCanvas,
  IssuesCard,
  LeaveGuard,
  NodeCard,
  RejectionView,
  useGraphEditing,
} from '@/modules/graph-editor';
import { catalogQuery, cloneNode, nodeQuery, saveNode, validateNode } from '@/modules/nodes';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

export function NodeEditorPage({ id }: { id: string }) {
  const node = useQuery(nodeQuery(id));
  const catalog = useQuery(catalogQuery);

  if (node.isError) {
    return <p className="p-6 text-muted">Составной узел не найден.</p>;
  }

  if (!node.data || !catalog.data) {
    return <p className="p-6 text-muted">Загрузка…</p>;
  }

  return (
    <NodeEditor
      key={id}
      id={id}
      builtIn={node.data.group === 'builtIn'}
      saved={{ name: node.data.title, definition: node.data.definition as unknown as CompositeDef }}
      catalog={catalog.data as CatalogNode[]}
    />
  );
}

type Panel = 'declarations' | 'issues' | undefined;

function NodeEditor({
  id,
  builtIn,
  saved,
  catalog,
}: {
  id: string;
  /** A built-in node opens read only; it can only be cloned. */
  builtIn: boolean;
  saved: Edited<CompositeDef>;
  catalog: CatalogNode[];
}) {
  const navigate = useNavigate();
  const validate = useCallback(
    (edited: Edited<CompositeDef>, signal: AbortSignal) => validateNode(id, edited, signal),
    [id],
  );
  const editing = useGraphEditing({ saved, validate, draftKey: draftKey({ node: id }) });
  const queryClient = useQueryClient();
  const { name, definition } = editing;
  const [selected, setSelected] = useState<string>();
  const [panel, setPanel] = useState<Panel>();

  function open(next: Panel) {
    setPanel(panel === next ? undefined : next);
    setSelected(undefined);
  }

  async function save() {
    await editing.save(async (edited) => {
      await saveNode(id, edited);
      await queryClient.invalidateQueries({ queryKey: catalogQuery.queryKey });
    });
  }

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-canvas">
      <GraphCanvas
        definition={definition}
        catalog={catalog}
        issues={editing.issues}
        readOnly={builtIn}
        onChange={editing.setDefinition}
        onSelect={(nodeId) => {
          setSelected(nodeId);
          setPanel(undefined);
        }}
      />
      <FloatingBar label="Составной узел" side="left">
        <Link to="/nodes" className="rounded-md px-3 py-2 text-sm hover:bg-panel-2">
          ‹ Составные узлы
        </Link>
        <BarDivider />
        <span className="px-2 text-sm font-bold">{name || 'Без названия'}</span>
        {builtIn ? (
          <>
            <Badge tone="builtIn">встроенный</Badge>
            <BarDivider />
            <span className="px-2 text-xs text-muted">Только чтение</span>
          </>
        ) : (
          <Badge>мой</Badge>
        )}
      </FloatingBar>
      <FloatingBar label="Действия" side="right">
        <Button look="plain" aria-expanded={panel === 'issues'} onClick={() => open('issues')}>
          Ошибки
          <span
            className={`grid h-[18px] min-w-5 place-items-center rounded-full px-1 text-[11px] font-bold text-white ${editing.errorCount > 0 ? 'bg-err' : 'bg-ok'}`}
          >
            {editing.errorCount}
          </span>
        </Button>
        <Button
          look="plain"
          aria-expanded={panel === 'declarations'}
          onClick={() => open('declarations')}
        >
          Входы и выходы
        </Button>
        <BarDivider />
        {builtIn ? (
          <Button
            look="primary"
            onClick={() =>
              void cloneNode(id).then((clone) =>
                navigate({ to: '/nodes/$id', params: { id: clone } }),
              )
            }
          >
            Клонировать и править
          </Button>
        ) : (
          <>
            <Button look="plain" disabled={!editing.dirty} onClick={editing.discard}>
              Отменить правки
            </Button>
            <Button look="primary" disabled={!editing.canSave} onClick={() => void save()}>
              Сохранить
            </Button>
          </>
        )}
      </FloatingBar>
      {selected && (
        <NodeCard
          definition={definition}
          catalog={catalog}
          nodeId={selected}
          readOnly={builtIn}
          onChange={editing.setDefinition}
          onClose={() => setSelected(undefined)}
        />
      )}
      {panel === 'declarations' && (
        <DeclarationsCard
          name={name}
          definition={definition}
          catalog={catalog}
          readOnly={builtIn}
          onName={editing.setName}
          onChange={editing.setDefinition}
          onClose={() => setPanel(undefined)}
        />
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
      {builtIn && (
        <p
          role="note"
          className="absolute top-[76px] left-1/2 z-10 w-[640px] -translate-x-1/2 rounded-[10px] border border-[#33405a] bg-[#1e2532] px-4 py-3 text-sm shadow-xl"
        >
          Встроенный узел нельзя править. Клон — независимая копия в «Мои составные»: её можно
          менять и использовать вместо этого узла.
        </p>
      )}
      <LeaveGuard dirty={editing.dirty} />
      {editing.offer && (
        <DraftOfferDialog
          offer={editing.offer}
          what={`узла «${saved.name}»`}
          onRestore={editing.restoreDraft}
          onDrop={editing.dropDraft}
        />
      )}
      {editing.rejection && (
        <RejectionView rejection={editing.rejection} onClose={editing.dismissRejection} />
      )}
    </div>
  );
}
