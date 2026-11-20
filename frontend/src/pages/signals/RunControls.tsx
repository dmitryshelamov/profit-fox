import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import type { Hypothesis } from '@/api/client';
import { enqueueRun, type RunAvailability } from '@/api/runs';
import { buttonClass } from '@/modules/graph-editor/ui/Card';

import { kindLabel, periodLabel, periodOrder } from '../runs/labels';

/**
 * «Прогнать» with the choice of the period and «Сверка» of one hypothesis (spec bench-run-queue «Кнопки прогона в списке
 * гипотез»). What the ledger rules allow comes from the API; a period that is not allowed is disabled and the reason of
 * the API stands next to the buttons. A queued run opens the section «Прогоны».
 */
export function RunControls({ hypothesis }: { readonly hypothesis: Hypothesis }) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const available = (period: string, kind: string): RunAvailability | undefined =>
    hypothesis.runs.find((run) => run.period === period && run.kind === kind);
  const verdictOpen = (period: string) => available(period, 'verdict')?.canRun === true;

  const [chosen, setChosen] = useState<string | null>(null);
  const [refusal, setRefusal] = useState<{ reason: string; runId: string | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const period = chosen ?? periodOrder.find(verdictOpen) ?? periodOrder[0];
  // A parity run is blind and reads only exploration (spec hypothesis-bench «Сверочный прогон»).
  const parityPeriod = 'exploration';

  const reasons = hypothesis.runs
    .filter(
      (run) => !run.canRun && run.reason && (run.kind === 'verdict' || run.period === parityPeriod),
    )
    .sort(
      (a, b) =>
        periodOrder.indexOf(a.period as never) - periodOrder.indexOf(b.period as never) ||
        a.kind.localeCompare(b.kind),
    );

  const enqueue = async (kind: 'verdict' | 'parity') => {
    setBusy(true);
    setRefusal(null);
    try {
      const outcome = await enqueueRun({
        hypothesis: hypothesis.id,
        version: Number(hypothesis.version),
        period: kind === 'parity' ? parityPeriod : period,
        kind,
      });
      if (!outcome.ok) {
        setRefusal({ reason: outcome.reason, runId: outcome.runId });
        return;
      }

      await queries.invalidateQueries({ queryKey: ['runs'] });
      await queries.invalidateQueries({ queryKey: ['hypotheses'] });
      await navigate({ to: '/runs' });
    } catch (problem) {
      setRefusal({
        reason: problem instanceof Error ? problem.message : String(problem),
        runId: null,
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="ml-auto flex flex-col items-end gap-1">
      <div className="flex items-center gap-2">
        <select
          aria-label={`Период ${hypothesis.id}`}
          className={buttonClass}
          value={period}
          onChange={(event) => setChosen(event.target.value)}
        >
          {periodOrder.map((option) => (
            <option key={option} value={option} disabled={!verdictOpen(option)}>
              {periodLabel(option)}
            </option>
          ))}
        </select>
        <button
          type="button"
          className={buttonClass}
          aria-label={`Прогнать ${hypothesis.id}`}
          disabled={busy || !verdictOpen(period)}
          onClick={() => void enqueue('verdict')}
        >
          Прогнать
        </button>
        <button
          type="button"
          className={buttonClass}
          aria-label={`Сверка ${hypothesis.id}`}
          disabled={busy || available(parityPeriod, 'parity')?.canRun !== true}
          onClick={() => void enqueue('parity')}
        >
          Сверка
        </button>
      </div>
      {reasons.length > 0 && (
        <ul aria-label={`Почему нельзя ${hypothesis.id}`} className="text-xs text-zinc-400">
          {reasons.map((run) => (
            <li key={`${run.period}-${run.kind}`} className="text-right">
              {periodLabel(run.period)}, {kindLabel(run.kind)}: {run.reason}
              {run.runId && (
                <>
                  {' '}
                  <Link
                    to="/runs/$id"
                    params={{ id: run.runId }}
                    className="text-amber-300 hover:underline"
                  >
                    прогон
                  </Link>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
      {refusal && (
        <p role="alert" className="text-xs text-red-300">
          {refusal.reason}
          {refusal.runId && (
            <>
              {' '}
              <Link
                to="/runs/$id"
                params={{ id: refusal.runId }}
                className="text-amber-300 hover:underline"
              >
                прогон
              </Link>
            </>
          )}
        </p>
      )}
    </div>
  );
}
