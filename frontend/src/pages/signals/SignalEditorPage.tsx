import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';

import { api, ApiError, type RequestError } from '@/api/client';
import { listNodeKinds, written } from '@/api/signals';
import { signalRules } from '@/modules/graph-editor/document';
import type { Graph } from '@/modules/graph-editor/model/types';
import {
  type Content,
  DocumentEditor,
  type DocumentType,
  newContent,
  Problem,
  toLayout,
} from '@/pages/documents/DocumentEditor';
import { openStrategyOfSignal } from '@/pages/strategies/fromSignal';
import { PreviewPanel } from '@/preview/PreviewPanel';
import { Button } from '@/shared/ui/Button';

export { draftDelayMs } from '@/pages/documents/DocumentEditor';

const section = { to: '/signals', title: 'Сигналы' } as const;

/** The editor of a signal: its rules, its words and the requests of `/api/signals`. */
const signalType: DocumentType = {
  rules: signalRules,
  section: '/signals',
  sectionTitle: 'Сигналы',
  newTitle: 'Новый сигнал',
  nameLabel: 'Имя сигнала',
  namePlaceholder: 'funding-high',
  queryKey: 'signal',
  listKey: 'signals',
  create: async (body) => written(await api.POST('/api/signals', { body })),
  replace: async (name, body) =>
    written(await api.PUT('/api/signals/{name}', { params: { path: { name } }, body })),
  open: async (navigate, name) => {
    await navigate({ to: '/signals/$name', params: { name }, replace: true });
  },
};

/** The editor of a saved signal, `/signals/<name>`. */
export function SavedSignalPage({ name }: { readonly name: string }) {
  const navigate = useNavigate();
  const catalog = useQuery({ queryKey: ['nodes'], queryFn: listNodeKinds });
  const signal = useQuery({
    queryKey: ['signal', name],
    queryFn: async () => {
      const answer = await api.GET('/api/signals/{name}', { params: { path: { name } } });
      if (answer.response.status === 422 && answer.error && 'errors' in answer.error) {
        return {
          broken: (answer.error.errors as RequestError[]).map((error) => error.reason).join('; '),
        };
      }

      if (!answer.response.ok) {
        throw new ApiError(
          answer.response.status,
          answer.response.status === 404 ? `Сигнала ${name} нет.` : 'API стенда не ответил.',
        );
      }

      const found = answer.data!;
      return {
        id: found.id,
        version: Number(found.version),
        content: {
          description: found.description ?? '',
          document: { graph: found.graph as unknown as Graph, layout: toLayout(found.layout) },
        } satisfies Content,
      };
    },
    staleTime: Infinity,
  });

  if (catalog.error || signal.error) {
    return <Problem section={section} text={(catalog.error ?? signal.error)!.message} />;
  }

  if (!catalog.data || !signal.data) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  if ('broken' in signal.data) {
    return (
      <Problem
        section={section}
        text={`Файл сигнала ${name} не читается, сохранить его нельзя: ${signal.data.broken}`}
      />
    );
  }

  const { id, version } = signal.data;
  return (
    <DocumentEditor
      key={name}
      type={signalType}
      saved={{ name }}
      startVersion={version}
      catalog={catalog.data}
      start={signal.data.content!}
      actions={
        id && (
          <Button
            onClick={() =>
              void openStrategyOfSignal(navigate, {
                id,
                name,
                version: version!,
                output: signal.data.content!.document.graph.outputs[0]?.type ?? null,
              })
            }
          >
            Создать стратегию
          </Button>
        )
      }
      bottom={({ graph, canRun, hasErrors, rememberAs }) => (
        <PreviewPanel graph={graph} canRun={canRun} hasErrors={hasErrors} rememberAs={rememberAs} />
      )}
    />
  );
}

/** The editor of a new signal, `/signals/new/<draft id>`: a draft until its first save, which asks for its name. */
export function NewSignalPage({ draftId }: { readonly draftId: string }) {
  const catalog = useQuery({ queryKey: ['nodes'], queryFn: listNodeKinds });
  const start = useMemo(() => newContent(signalRules, draftId), [draftId]);

  if (catalog.error) {
    return <Problem section={section} text={catalog.error.message} />;
  }

  if (!catalog.data) {
    return <Problem section={section} text="Загрузка…" quiet />;
  }

  return (
    <DocumentEditor
      key={draftId}
      type={signalType}
      saved={null}
      draftId={draftId}
      catalog={catalog.data}
      start={start}
      bottom={({ graph, canRun, hasErrors, rememberAs }) => (
        <PreviewPanel graph={graph} canRun={canRun} hasErrors={hasErrors} rememberAs={rememberAs} />
      )}
    />
  );
}
