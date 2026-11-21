import { useState } from 'react';

import { asGraph, graphText } from '../model/graph';
import type { Graph } from '../model/types';
import { buttonClass, Card, fieldClass } from './Card';

/**
 * The graph as JSON in the format of spec strategy-graph, without positions (spec signal-constructor «Карточка
 * JSON»). Pasted JSON replaces the graph only when it reads; the places then come from the layout by depth.
 */
export function JsonCard({
  graph,
  readOnly,
  onReplace,
  onClose,
}: {
  readonly graph: Graph;
  readonly readOnly: boolean;
  readonly onReplace: (graph: Graph) => void;
  readonly onClose: () => void;
}) {
  const [text, setText] = useState(() => graphText(graph));
  const [problem, setProblem] = useState<string | null>(null);

  const replace = () => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch (error) {
      setProblem(`Это не JSON: ${String(error)}`);
      return;
    }

    const read = asGraph(parsed);
    if (read === null) {
      setProblem('Это не граф: нужен объект с formatVersion 1 и массивом nodes.');
      return;
    }

    setProblem(null);
    onReplace(read);
  };

  return (
    <Card title="JSON" onClose={onClose}>
      <textarea
        aria-label="Граф JSON"
        className={`${fieldClass} h-96`}
        spellCheck={false}
        value={text}
        readOnly={readOnly}
        onChange={(event) => setText(event.target.value)}
      />
      {problem && <p className="text-xs text-red-300">{problem}</p>}
      <span className="flex gap-2">
        <button
          type="button"
          className={buttonClass}
          onClick={() => {
            setText(graphText(graph));
            setProblem(null);
          }}
        >
          Показать текущий
        </button>
        {!readOnly && (
          <button type="button" className={buttonClass} onClick={replace}>
            Заменить граф
          </button>
        )}
      </span>
    </Card>
  );
}
