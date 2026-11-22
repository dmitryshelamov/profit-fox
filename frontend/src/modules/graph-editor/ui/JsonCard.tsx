import { useState } from 'react';

import { Button } from '@/shared/ui/Button';

import { asGraph, graphText } from '../model/graph';
import type { Graph } from '../model/types';
import { Card } from './Card';

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
        className="h-96 resize-none rounded-md border border-line bg-panel-2 p-2.5 font-mono text-xs text-text outline-none focus:border-accent"
        spellCheck={false}
        value={text}
        readOnly={readOnly}
        onChange={(event) => setText(event.target.value)}
      />
      {problem && <p className="text-xs text-err">{problem}</p>}
      <span className="flex gap-2">
        <Button
          size="sm"
          onClick={() => {
            setText(graphText(graph));
            setProblem(null);
          }}
        >
          Показать текущий
        </Button>
        {!readOnly && (
          <Button size="sm" onClick={replace}>
            Заменить граф
          </Button>
        )}
      </span>
    </Card>
  );
}
