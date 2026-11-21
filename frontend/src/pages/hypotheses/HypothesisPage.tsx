import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';

import { api, type RequestError, unwrap } from '@/api/client';
import { listNodeKinds, written } from '@/api/signals';
import { signalRules } from '@/modules/graph-editor/document';
import { useGraphCheck } from '@/modules/graph-editor/editing/useGraphCheck';
import type { Graph, GraphDocument } from '@/modules/graph-editor/model/types';
import { BarDivider } from '@/modules/graph-editor/ui/FloatingBar';
import { GraphEditor } from '@/modules/graph-editor/ui/GraphEditor';
import { SectionLink } from '@/pages/documents/DocumentEditor';
import { PreviewPanel } from '@/preview/PreviewPanel';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { TextField } from '@/shared/ui/TextField';

/**
 * The signal of a hypothesis of the bench, read only (spec signal-constructor «Гипотеза только для чтения»): no edit,
 * no save, no draft, but the preview; «Скопировать в сигнал» makes a new signal with the same graph.
 */
export function HypothesisPage({ id }: { readonly id: string }) {
  const navigate = useNavigate();
  const catalog = useQuery({ queryKey: ['nodes'], queryFn: listNodeKinds });
  const hypotheses = useQuery({
    queryKey: ['hypotheses'],
    queryFn: async () => unwrap(await api.GET('/api/hypotheses')),
  });
  const hypothesis = hypotheses.data?.find((item) => item.id === id);
  const graph = (hypothesis?.signal ?? null) as Graph | null;
  const document = useMemo(
    (): GraphDocument | null => (graph ? { graph, layout: {} } : null),
    [graph],
  );
  const [name, setName] = useState('');
  const [errors, setErrors] = useState<readonly RequestError[]>([]);

  const problem = catalog.error ?? hypotheses.error;
  if (problem || (hypotheses.data && !hypothesis)) {
    return <Message text={problem?.message ?? `Гипотезы ${id} нет.`} />;
  }

  if (!catalog.data || !hypothesis) {
    return <Message text="Загрузка…" />;
  }

  if (!document) {
    return <Message text={`Сигнал гипотезы ${id} не читается: ${hypothesis.error ?? ''}`} />;
  }

  const copy = async () => {
    setErrors([]);
    const outcome = await written(
      await api.POST('/api/signals', {
        body: { name, description: null, graph: document.graph as never, layout: {} },
      }),
    );
    if (!outcome.ok) {
      setErrors(outcome.errors);
      return;
    }

    await navigate({ to: '/signals/$name', params: { name } });
  };

  return (
    <ReadOnlyEditor
      title={
        <>
          <SectionLink to="/signals" title="Сигналы" />
          <BarDivider />
          <span className="font-mono font-semibold">
            <span className="text-accent">{hypothesis.id}</span> {hypothesis.name}
          </span>
          <Badge tone="muted">только чтение</Badge>
        </>
      }
      document={document}
      catalog={catalog.data}
      timeframe={hypothesis.timeframe ?? '4h'}
      rememberAs={`hypothesis:${id}`}
      actions={
        <form
          className="flex items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void copy();
          }}
        >
          <TextField
            aria-label="Имя нового сигнала"
            mono
            className="w-44"
            placeholder="имя-сигнала"
            value={name}
            onChange={(event) => setName(event.target.value.trim())}
          />
          <Button type="submit" variant="primary" disabled={name === ''}>
            Скопировать в сигнал
          </Button>
          {errors.length > 0 && (
            <span role="alert" className="max-w-64 truncate px-1.5 text-xs text-err">
              {errors.map((error) => error.reason).join('; ')}
            </span>
          )}
        </form>
      }
    />
  );
}

function ReadOnlyEditor({
  title,
  document,
  catalog,
  timeframe,
  rememberAs,
  actions,
}: {
  readonly title: React.ReactNode;
  readonly document: GraphDocument;
  readonly catalog: Parameters<typeof GraphEditor>[0]['catalog'];
  readonly timeframe: string;
  readonly rememberAs: string;
  readonly actions: React.ReactNode;
}) {
  const check = useGraphCheck(document.graph);
  return (
    <GraphEditor
      title={title}
      document={document}
      catalog={catalog}
      rules={signalRules}
      errors={check.errors}
      checkFailed={check.failed}
      readOnly
      actions={actions}
      onChange={() => undefined}
      bottom={
        <PreviewPanel
          graph={document.graph}
          canRun={check.checked && check.errors.length === 0}
          hasErrors={check.checked && check.errors.length > 0}
          rememberAs={rememberAs}
          defaultTimeframe={timeframe}
        />
      }
    />
  );
}

function Message({ text }: { readonly text: string }) {
  return (
    <main className="flex min-h-screen flex-col items-start gap-3 bg-canvas p-6">
      <SectionLink to="/signals" title="Сигналы" />
      <p className="text-muted">{text}</p>
    </main>
  );
}
