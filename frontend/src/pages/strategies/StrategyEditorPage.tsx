import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';

import { api, ApiError, type RequestError } from '@/api/client';
import { written } from '@/api/signals';
import { listStrategyNodeKinds } from '@/api/strategies';
import { strategyRules } from '@/modules/graph-editor/document';
import type { SignalUse } from '@/modules/graph-editor/model/signals';
import type { Graph } from '@/modules/graph-editor/model/types';
import {
  type Content,
  DocumentEditor,
  type DocumentType,
  newContent,
  Problem,
  toLayout,
} from '@/pages/documents/DocumentEditor';
import { PreviewPanel } from '@/preview/PreviewPanel';

const section = { to: '/strategies', title: 'Стратегии' } as const;

/** The editor of a strategy: its rules, its words and the requests of `/api/strategies`. */
const strategyType: DocumentType = {
  rules: strategyRules,
  section: '/strategies',
  sectionTitle: 'Стратегии',
  newTitle: 'Новая стратегия',
  nameLabel: 'Имя стратегии',
  namePlaceholder: 'funding-short',
  queryKey: 'strategy',
  listKey: 'strategies',
  create: async (body) => written(await api.POST('/api/strategies', { body })),
  replace: async (name, body) =>
    written(await api.PUT('/api/strategies/{name}', { params: { path: { name } }, body })),
  open: async (navigate, name) => {
    await navigate({ to: '/strategies/$name', params: { name }, replace: true });
  },
};

/** The editor of a saved strategy, `/strategies/<name>`. */
export function SavedStrategyPage({ name }: { readonly name: string }) {
  const catalog = useQuery({ queryKey: ['nodes', 'strategy'], queryFn: listStrategyNodeKinds });
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

  if (catalog.error || strategy.error) {
    return <Problem section={section} text={(catalog.error ?? strategy.error)!.message} />;
  }

  if (!catalog.data || !strategy.data) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  if ('broken' in strategy.data) {
    return (
      <Problem
        section={section}
        text={`Стратегия ${name} не читается, сохранить её нельзя: ${strategy.data.broken}`}
      />
    );
  }

  return (
    <DocumentEditor
      key={name}
      type={strategyType}
      saved={{ name }}
      startVersion={strategy.data.version}
      raised={strategy.data.raised}
      catalog={catalog.data}
      start={strategy.data.content!}
      startSignals={strategy.data.signals}
      bottom={bottom}
    />
  );
}

/** The editor of a new strategy, `/strategies/new/<draft id>`: a draft until its first save, which asks for its name. */
export function NewStrategyPage({ draftId }: { readonly draftId: string }) {
  const catalog = useQuery({ queryKey: ['nodes', 'strategy'], queryFn: listStrategyNodeKinds });
  const start = useMemo(() => newContent(strategyRules, draftId), [draftId]);

  if (catalog.error) {
    return <Problem section={section} text={catalog.error.message} />;
  }

  if (!catalog.data) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  return (
    <DocumentEditor
      key={draftId}
      type={strategyType}
      saved={null}
      draftId={draftId}
      catalog={catalog.data}
      start={start}
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
