import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker, useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';

import { api, ApiError, type RequestError } from '@/api/client';
import { listNodeKinds, written } from '@/api/signals';
import { signalRules } from '@/modules/graph-editor/document';
import { draftKey, readDraft, removeDraft, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { useGraphCheck } from '@/modules/graph-editor/editing/useGraphCheck';
import { differences, sameDocument } from '@/modules/graph-editor/model/compare';
import { layoutOfNodes } from '@/modules/graph-editor/model/operations';
import type { Graph, GraphDocument, Layout, NodeKind } from '@/modules/graph-editor/model/types';
import { buttonClass, fieldClass } from '@/modules/graph-editor/ui/Card';
import { DraftQuestion } from '@/modules/graph-editor/ui/DraftQuestion';
import { GraphEditor } from '@/modules/graph-editor/ui/GraphEditor';
import { PreviewPanel } from '@/preview/PreviewPanel';

/** The delay after an edit before the draft is written to the browser. */
export const draftDelayMs = 300;

/** What a signal file holds apart from its name. */
interface Content {
  readonly description: string;
  readonly document: GraphDocument;
}

/** The editor of a saved signal, `/signals/<name>`. */
export function SavedSignalPage({ name }: { readonly name: string }) {
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
        content: {
          description: found.description ?? '',
          document: { graph: found.graph as unknown as Graph, layout: toLayout(found.layout) },
        } satisfies Content,
      };
    },
    staleTime: Infinity,
  });

  if (catalog.error || signal.error) {
    return <Problem text={(catalog.error ?? signal.error)!.message} />;
  }

  if (!catalog.data || !signal.data) {
    return <Problem text="Загрузка…" quiet />;
  }

  if ('broken' in signal.data) {
    return (
      <Problem
        text={`Файл сигнала ${name} не читается, сохранить его нельзя: ${signal.data.broken}`}
      />
    );
  }

  return (
    <SignalEditor key={name} saved={{ name }} catalog={catalog.data} start={signal.data.content!} />
  );
}

/** The editor of a new signal, `/signals/new/<draft id>`: a draft until its first save, which asks for its name. */
export function NewSignalPage({ draftId }: { readonly draftId: string }) {
  const catalog = useQuery({ queryKey: ['nodes'], queryFn: listNodeKinds });
  const start = useMemo((): Content => {
    const draft = readDraft(draftKey({ newId: draftId }));
    return draft
      ? { description: draft.description, document: { graph: draft.graph, layout: draft.layout } }
      : { description: '', document: { graph: signalRules.start(), layout: {} } };
  }, [draftId]);

  if (catalog.error) {
    return <Problem text={catalog.error.message} />;
  }

  if (!catalog.data) {
    return <Problem text="Загрузка…" quiet />;
  }

  return (
    <SignalEditor
      key={draftId}
      saved={null}
      draftId={draftId}
      catalog={catalog.data}
      start={start}
    />
  );
}

/**
 * The editor of one signal: the canvas, the check, the draft in the browser, saving, and the preview panel. A saved
 * signal opened with a draft that differs from its file asks to restore or discard it.
 */
function SignalEditor({
  saved,
  draftId,
  catalog,
  start,
}: {
  readonly saved: { readonly name: string } | null;
  readonly draftId?: string;
  readonly catalog: readonly NodeKind[];
  readonly start: Content;
}) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const key = saved ? draftKey(saved) : draftKey({ newId: draftId! });
  const [file, setFile] = useState<Content | null>(saved ? start : null);
  const [content, setContent] = useState<Content>(start);
  const [question, setQuestion] = useState(() => {
    if (!saved) {
      return null;
    }

    const draft = readDraft(key);
    const drafted = draft && {
      description: draft.description,
      document: { graph: draft.graph, layout: draft.layout },
    };
    return drafted && !sameContent(drafted, start)
      ? { draft: drafted, savedAt: draft.savedAt }
      : null;
  });
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState('');
  const [saveErrors, setSaveErrors] = useState<readonly RequestError[]>([]);
  const [saving, setSaving] = useState(false);
  const check = useGraphCheck(content.document.graph);

  const dirty = file === null ? true : !sameContent(content, file);
  const canSave = dirty && check.checked && check.errors.length === 0 && !saving;

  useEffect(() => {
    if (question) {
      return;
    }

    const timer = setTimeout(() => {
      if (file !== null && sameContent(content, file)) {
        removeDraft(key);
      } else {
        writeDraft(key, { description: content.description, ...content.document });
      }
    }, draftDelayMs);
    return () => clearTimeout(timer);
  }, [content, file, key, question]);

  // The way from a new signal to its saved page right after the first save is not leaving it.
  const savedAway = useRef(false);
  useBlocker({
    shouldBlockFn: () =>
      dirty &&
      !savedAway.current &&
      !window.confirm('Есть несохранённые правки. Уйти? Черновик останется в браузере.'),
    enableBeforeUnload: () => dirty && !savedAway.current,
  });

  const save = async (as?: string) => {
    setSaving(true);
    setSaveErrors([]);
    const body = {
      description: content.description.trim() === '' ? null : content.description.trim(),
      graph: content.document.graph as never,
      layout: layoutOfNodes(content.document) as never,
    };
    try {
      const outcome = saved
        ? await written(
            await api.PUT('/api/signals/{name}', { params: { path: { name: saved.name } }, body }),
          )
        : await written(await api.POST('/api/signals', { body: { ...body, name: as ?? '' } }));
      if (!outcome.ok) {
        setSaveErrors(outcome.errors);
        return;
      }

      removeDraft(key);
      await queries.invalidateQueries({ queryKey: ['signals'] });
      if (saved) {
        queries.setQueryData(['signal', saved.name], { content });
        setFile(content);
      } else {
        const created = outcome.value as { name: string };
        queries.removeQueries({ queryKey: ['signal', created.name] });
        savedAway.current = true;
        await navigate({ to: '/signals/$name', params: { name: created.name }, replace: true });
      }
    } catch (problem) {
      setSaveErrors([
        {
          code: 'SaveFailed',
          reason: String(problem instanceof Error ? problem.message : problem),
        },
      ]);
    } finally {
      setSaving(false);
    }
  };

  const nameError = saveErrors.find((error) => error.field === 'name');
  const otherErrors = saveErrors.filter((error) => error.field !== 'name');

  return (
    <>
      <GraphEditor
        title={
          <>
            <Link to="/signals" className="text-zinc-400 hover:text-zinc-100">
              ← Сигналы
            </Link>
            <span className="font-mono font-semibold">{saved?.name ?? 'Новый сигнал'}</span>
            <input
              aria-label="Описание"
              className={`${fieldClass} w-64 font-sans`}
              placeholder="описание"
              value={content.description}
              onChange={(event) => setContent({ ...content, description: event.target.value })}
            />
            <span role="status" aria-label="Состояние" className="text-xs text-zinc-400">
              {!check.checked && !check.failed
                ? 'проверяю…'
                : check.failed
                  ? 'проверка не удалась'
                  : dirty
                    ? 'не сохранено'
                    : 'сохранено'}
            </span>
          </>
        }
        document={content.document}
        catalog={catalog}
        rules={signalRules}
        errors={check.errors}
        checkFailed={check.failed}
        readOnly={false}
        onChange={(document) => setContent((current) => ({ ...current, document }))}
        actions={
          <>
            {file !== null && (
              <button
                type="button"
                className={buttonClass}
                disabled={!dirty}
                onClick={() => {
                  setContent(file);
                  removeDraft(key);
                }}
              >
                Отбросить
              </button>
            )}
            <button
              type="button"
              className={`${buttonClass} border-amber-500`}
              disabled={!canSave}
              onClick={() => (saved ? void save() : setNaming(true))}
            >
              {saving ? 'Сохраняю…' : 'Сохранить'}
            </button>
            {otherErrors.length > 0 && (
              <span className="text-xs text-red-300">
                {otherErrors.map((error) => error.reason).join('; ')}
              </span>
            )}
          </>
        }
        bottom={
          <PreviewPanel
            graph={content.document.graph}
            canRun={check.checked && check.errors.length === 0}
            rememberAs={saved?.name ?? `new:${draftId}`}
          />
        }
      />
      {question && (
        <DraftQuestion
          savedAt={question.savedAt}
          {...differences(start.document, question.draft.document)}
          onRestore={() => {
            setContent(question.draft);
            setQuestion(null);
          }}
          onDiscard={() => {
            removeDraft(key);
            setQuestion(null);
          }}
        />
      )}
      {naming && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/60">
          <form
            role="dialog"
            aria-label="Имя сигнала"
            className="flex w-96 flex-col gap-3 rounded-lg border border-zinc-700 bg-zinc-900 p-4 text-sm"
            onSubmit={async (event) => {
              event.preventDefault();
              await save(name);
            }}
          >
            <label className="flex flex-col gap-1">
              Имя сигнала
              <input
                aria-label="Имя"
                className={fieldClass}
                placeholder="funding-high"
                value={name}
                autoFocus
                onChange={(event) => setName(event.target.value.trim())}
              />
              <span className="text-xs text-zinc-500">
                Строчные латинские буквы и цифры через дефис.
              </span>
            </label>
            {nameError && <p className="text-xs text-red-300">{nameError.reason}</p>}
            <span className="flex justify-end gap-2">
              <button type="button" className={buttonClass} onClick={() => setNaming(false)}>
                Отмена
              </button>
              <button
                type="submit"
                className={`${buttonClass} border-amber-500`}
                disabled={name === '' || saving}
              >
                Сохранить
              </button>
            </span>
          </form>
        </div>
      )}
    </>
  );
}

function sameContent(a: Content, b: Content): boolean {
  return a.description.trim() === b.description.trim() && sameDocument(a.document, b.document);
}

function toLayout(
  layout: Readonly<Record<string, { x: number | string; y: number | string }>>,
): Layout {
  return Object.fromEntries(
    Object.entries(layout).map(([id, place]) => [id, { x: Number(place.x), y: Number(place.y) }]),
  );
}

function Problem({ text, quiet = false }: { readonly text: string; readonly quiet?: boolean }) {
  return (
    <main className="flex min-h-screen flex-col items-start gap-3 bg-zinc-950 p-6 text-sm text-zinc-100">
      <Link to="/signals" className="text-zinc-400 hover:text-zinc-100">
        ← Сигналы
      </Link>
      <p className={quiet ? 'text-zinc-400' : 'text-red-300'}>{text}</p>
    </main>
  );
}
