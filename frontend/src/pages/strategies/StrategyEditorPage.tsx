import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';

import { api, ApiError, type RequestError } from '@/api/client';
import { written } from '@/api/signals';
import { listStrategyNodeKinds } from '@/api/strategies';
import {
  type StrategyKind,
  strategyKindOf,
  strategyRulesOf,
} from '@/modules/graph-editor/document';
import { readDraft, strategyDraftKey } from '@/modules/graph-editor/drafts/drafts';
import type { SignalUse } from '@/modules/graph-editor/model/signals';
import type { Graph } from '@/modules/graph-editor/model/types';
import {
  type Content,
  DocumentEditor,
  type DocumentType,
  type EditorState,
  newContent,
  Problem,
  toLayout,
} from '@/pages/documents/DocumentEditor';
import { PreviewPanel } from '@/preview/PreviewPanel';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

import { wordsOf } from './kinds';

const section = { to: '/strategies', title: 'Стратегии' } as const;

/** The editor of a strategy of a kind: its rules, its words and the requests of `/api/strategies`. */
function strategyTypeOf(kind: StrategyKind): DocumentType {
  return {
    rules: strategyRulesOf(kind),
    section: '/strategies',
    sectionTitle: 'Стратегии',
    newTitle: 'Новая стратегия',
    nameLabel: 'Имя стратегии',
    namePlaceholder: 'funding-short',
    queryKey: 'strategy',
    listKey: 'strategies',
    create: async (body) => written(await api.POST('/api/strategies', { body: { ...body, kind } })),
    replace: async (name, body) =>
      written(
        await api.PUT('/api/strategies/{name}', {
          params: { path: { name } },
          body: { ...body, kind },
        }),
      ),
    open: async (navigate, name) => {
      await navigate({ to: '/strategies/$name', params: { name }, replace: true });
    },
  };
}

/** The node catalog of a strategy of a kind, under a key of its own. */
function useCatalog(kind: StrategyKind | undefined) {
  return useQuery({
    queryKey: ['nodes', 'strategy', kind],
    queryFn: () => listStrategyNodeKinds(kind!),
    enabled: kind !== undefined,
  });
}

/** The kind of the strategy in the plaque of its document (spec strategy-constructor «Вид в списке и в редакторе»). */
function KindPlaque({ kind }: { readonly kind: StrategyKind }) {
  return (
    <span aria-label="Вид" className="inline-flex">
      <Badge tone="muted">{wordsOf(kind).title}</Badge>
    </span>
  );
}

/**
 * «Прогнать» of the editor (spec strategy-constructor «Кнопка «Прогнать»»): opens the form of a run of the latest saved
 * version; with edits that are not saved, before the first save and with errors in the graph it is disabled and says
 * why.
 */
function RunAction({ name, state }: { readonly name: string | null; readonly state: EditorState }) {
  const navigate = useNavigate();
  const reason =
    name === null || state.dirty
      ? 'сначала сохраните стратегию'
      : state.hasErrors
        ? 'в графе есть ошибки'
        : null;
  return (
    <>
      <Button
        disabled={reason !== null}
        title={reason ?? undefined}
        aria-describedby={reason === null ? undefined : 'run-reason'}
        onClick={() =>
          name !== null &&
          void navigate({
            to: '/strategies/$name/run',
            params: { name },
            search: state.version === undefined ? {} : { version: state.version },
          })
        }
      >
        Прогнать
      </Button>
      {reason !== null && (
        <span id="run-reason" className="sr-only">
          {reason}
        </span>
      )}
    </>
  );
}

/** The editor of a saved strategy, `/strategies/<name>`. */
export function SavedStrategyPage({ name }: { readonly name: string }) {
  const strategy = useQuery({
    queryKey: ['strategy', name],
    queryFn: async () => {
      const answer = await api.GET('/api/strategies/{name}', { params: { path: { name } } });
      if (answer.response.status === 422 && answer.error && 'errors' in answer.error) {
        return {
          broken: (answer.error.errors as RequestError[]).map((error) => error.reason).join('; '),
        };
      }

      if (!answer.response.ok) {
        throw new ApiError(
          answer.response.status,
          answer.response.status === 404 ? `Стратегии ${name} нет.` : 'API стенда не ответил.',
        );
      }

      const found = answer.data!;
      return {
        version: Number(found.version),
        kind: strategyKindOf(found.kind),
        // The store keeps the form before the node of the position; this graph is that one raised on reading.
        raised: found.raised === true,
        signals: found.signals as SignalUse[],
        content: {
          description: found.description ?? '',
          document: { graph: found.graph as unknown as Graph, layout: toLayout(found.layout) },
        } satisfies Content,
      };
    },
    staleTime: Infinity,
  });
  const kind = strategy.data && 'kind' in strategy.data ? strategy.data.kind : undefined;
  const catalog = useCatalog(kind);
  const type = useMemo(() => (kind === undefined ? null : strategyTypeOf(kind)), [kind]);

  if (catalog.error || strategy.error) {
    return <Problem section={section} text={(catalog.error ?? strategy.error)!.message} />;
  }

  if (strategy.data && 'broken' in strategy.data) {
    return (
      <Problem
        section={section}
        text={`Стратегия ${name} не читается, сохранить её нельзя: ${strategy.data.broken}`}
      />
    );
  }

  if (!catalog.data || !strategy.data || kind === undefined || type === null) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  return (
    <DocumentEditor
      key={name}
      type={type}
      saved={{ name }}
      startVersion={strategy.data.version}
      raised={strategy.data.raised}
      catalog={catalog.data}
      start={strategy.data.content!}
      startSignals={strategy.data.signals}
      plaque={<KindPlaque kind={kind} />}
      actions={(state) => <RunAction name={name} state={state} />}
      bottom={bottom}
    />
  );
}

/** The editor of a new strategy, `/strategies/new/<draft id>`: a draft until its first save, which asks for its name. */
export function NewStrategyPage({ draftId }: { readonly draftId: string }) {
  // The kind was chosen before the editor opened and lies in the draft; a draft of before the kinds is of ideas, as
  // the start of a new strategy was then.
  const kind = useMemo(
    () => readDraft(strategyDraftKey({ newId: draftId }))?.kind ?? 'idea',
    [draftId],
  );
  const type = useMemo(() => strategyTypeOf(kind), [kind]);
  const catalog = useCatalog(kind);
  const start = useMemo(() => newContent(type.rules, draftId), [type, draftId]);

  if (catalog.error) {
    return <Problem section={section} text={catalog.error.message} />;
  }

  if (!catalog.data) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  return (
    <DocumentEditor
      key={draftId}
      type={type}
      saved={null}
      draftId={draftId}
      catalog={catalog.data}
      start={start}
      plaque={<KindPlaque kind={kind} />}
      actions={(state) => <RunAction name={null} state={state} />}
      bottom={bottom}
    />
  );
}

/** The preview of the strategy on a pair at the bottom of the editor. */
function bottom({
  graph,
  canRun,
  hasErrors,
  rememberAs,
}: {
  graph: Graph;
  canRun: boolean;
  hasErrors: boolean;
  rememberAs: string;
}) {
  return (
    <PreviewPanel
      kind="strategy"
      graph={graph}
      canRun={canRun}
      hasErrors={hasErrors}
      rememberAs={`strategy:${rememberAs}`}
    />
  );
}
