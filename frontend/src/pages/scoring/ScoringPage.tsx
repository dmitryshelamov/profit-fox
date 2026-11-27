import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { ApiError } from '@/api/client';
import { type IdeaChoices, type IdeaScore, scoreIdeas } from '@/api/ideas';
import type { InstrumentKind } from '@/api/market';
import {
  listScoringLabels,
  scoreText,
  scoreTone,
  type ScoringLabel,
  setLabelWeight,
  weights,
} from '@/api/scoring';
import { listStrategies } from '@/api/strategies';
import { KindChoice } from '@/modules/instrument-kind/KindChoice';
import { periods } from '@/pages/ideas/IdeaChoices';
import { changeText, signedAmount, signTone } from '@/pages/market/format';
import { timeframes } from '@/preview/preview';
import { Badge } from '@/shared/ui/Badge';
import { Page, PageTitle } from '@/shared/ui/Page';
import { SelectField } from '@/shared/ui/SelectField';
import { Table, Td, Th, Tr } from '@/shared/ui/Table';

/** A score with fewer closed ideas than this is a small sample (spec idea-score «Итоги по баллу на странице «Скоринг»»). */
export const smallSample = 30;

const labelsKey = ['scoring', 'labels'] as const;
const scoresKey = ['scoring', 'scores'] as const;

/**
 * «Скоринг» (change add-idea-score, spec idea-score «Раздел «Скоринг»», «Итоги по баллу на странице «Скоринг»»): the
 * labels with their weights for a long and a short, each weight changed in its row, and below the outcomes of the closed
 * ideas by score with the choices of the journal. Labels are not made here: they are turned on in the list of signals.
 */
export function ScoringPage() {
  return (
    <Page>
      <PageTitle
        title="Скоринг"
        hint="Балл идеи — сумма весов её меток по её стороне. Веса меняют балл всех идей сразу, открытых и закрытых. Метки включают в списке сигналов."
      />
      <Weights />
      <Scores />
    </Page>
  );
}

function SectionHead({ title, note }: { readonly title: string; readonly note: string }) {
  return (
    <div className="flex items-baseline justify-between gap-6">
      <h2 className="text-base font-bold">{title}</h2>
      <span className="text-[12px] text-muted">{note}</span>
    </div>
  );
}

function Weights() {
  const labels = useQuery({ queryKey: labelsKey, queryFn: listScoringLabels });
  const rows = labels.data ?? [];
  const none = labels.data !== undefined && rows.every((row) => row.builtIn);

  return (
    <section aria-label="Веса меток" className="flex flex-col gap-4">
      <SectionHead title="Веса меток" note="Вес — целое от −3 до +3; нет веса — 0" />
      {labels.isError && (
        <p role="alert" className="text-err">
          {labels.error.message}
        </p>
      )}
      {labels.data && (
        <Table label="Веса меток">
          <thead>
            <tr>
              <Th>Метка</Th>
              <Th>ТФ</Th>
              <Th>Сигнал</Th>
              <Th>Вес лонга</Th>
              <Th>Вес шорта</Th>
              <Th>
                <span className="sr-only">Ошибка</span>
              </Th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <WeightRow key={row.text} row={row} />
            ))}
          </tbody>
        </Table>
      )}
      {none && (
        <p className="text-muted">
          Меток пока нет: метки включают в списке сигналов.{' '}
          <Link to="/signals" className="text-accent hover:underline">
            Сигналы
          </Link>
        </p>
      )}
    </section>
  );
}

// A weight chosen shows at once; a refusal puts the weight before back and tells why in the row (scenario «Отказ
// сервера»), and a label gone since the table was read rereads it (task 8.7). A weight saved rereads the outcomes by
// score (scenario «Правка веса»).
function WeightRow({ row }: { readonly row: ScoringLabel }) {
  const client = useQueryClient();
  const [draft, setDraft] = useState<{ long: number; short: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (next: { long: number; short: number }) =>
      setLabelWeight(row.text, next.long, next.short),
    onSuccess: (result) => {
      setDraft(null);
      if (result.ok) {
        setError(null);
        client.setQueryData<ScoringLabel[]>(labelsKey, (old) =>
          old?.map((label) => (label.text === result.value.text ? result.value : label)),
        );
        void client.invalidateQueries({ queryKey: scoresKey });
        void client.invalidateQueries({ queryKey: ['ideas'] });
      } else {
        setError(
          `Вес не сохранён: ${result.errors.map((problem) => problem.reason).join('; ')}. Стоит прежний вес.`,
        );
      }
    },
    onError: (failure) => {
      setDraft(null);
      if (failure instanceof ApiError && failure.status === 404) {
        // The label was renamed or its signal deleted while the page was open: the table is read again.
        setError('Вес не сохранён: такой метки больше нет. Таблица перечитана.');
        void client.invalidateQueries({ queryKey: labelsKey });
      } else {
        setError('Вес не сохранён: сервер не ответил. Стоит прежний вес.');
      }
    },
  });
  const long = draft?.long ?? Number(row.longWeight);
  const short = draft?.short ?? Number(row.shortWeight);
  const choose = (next: { long: number; short: number }) => {
    setDraft(next);
    save.mutate(next);
  };

  return (
    <Tr className="h-12" aria-label={row.text}>
      <Td>
        <Badge tone="muted">{row.text}</Badge>
      </Td>
      <Td className="font-mono text-muted">{row.timeframe ?? '—'}</Td>
      <Td>
        {row.builtIn ? (
          <span className="font-mono text-muted">встроенная метка</span>
        ) : (
          <span className="flex items-center justify-between gap-3">
            <span className={`font-mono ${row.enabled ? '' : 'text-faint'}`}>{row.signal}</span>
            {!row.enabled && <Badge tone="muted">выключена</Badge>}
          </span>
        )}
      </Td>
      <Td>
        <WeightChoice
          label={`Вес лонга ${row.text}`}
          value={long}
          failed={error !== null}
          onChange={(value) => choose({ long: value, short })}
        />
      </Td>
      <Td>
        <WeightChoice
          label={`Вес шорта ${row.text}`}
          value={short}
          failed={error !== null}
          onChange={(value) => choose({ long, short: value })}
        />
      </Td>
      <Td className="text-right text-[12px] text-err">
        {error !== null && <span role="alert">{error}</span>}
      </Td>
    </Tr>
  );
}

// The tone of a weight colors the text of its select: the class of the field goes to its frame.
const selectTone: Record<string, string> = {
  'text-ok': '[&>select]:text-ok',
  'text-err': '[&>select]:text-err',
  'text-muted': '[&>select]:text-muted',
};

function WeightChoice({
  label,
  value,
  failed,
  onChange,
}: {
  readonly label: string;
  readonly value: number;
  readonly failed: boolean;
  readonly onChange: (value: number) => void;
}) {
  return (
    <SelectField
      mono
      aria-label={label}
      aria-invalid={failed}
      className={`w-[84px] ${selectTone[scoreTone(value)] ?? ''}`}
      value={value}
      onChange={(event) => onChange(Number(event.target.value))}
    >
      {weights.map((weight) => (
        <option key={weight} value={weight}>
          {scoreText(weight)}
        </option>
      ))}
    </SelectField>
  );
}

function Scores() {
  const strategies = useQuery({ queryKey: ['strategies'], queryFn: listStrategies });
  const [choices, setChoices] = useState<IdeaChoices>({ period: 30 });
  const set = (change: IdeaChoices) => setChoices({ ...choices, ...change });
  const scores = useQuery({
    queryKey: [...scoresKey, choices],
    queryFn: () => scoreIdeas(choices),
    placeholderData: keepPreviousData,
  });
  const rows = scores.data?.rows ?? [];
  const total = rows.reduce((sum, row) => sum + Number(row.ideas), 0);

  return (
    <section aria-label="Итоги по баллу" className="flex flex-col gap-4">
      <SectionHead
        title="Итоги по баллу"
        note="Закрытые идеи, итог после издержек · по текущим весам"
      />
      <div className="flex flex-wrap items-center gap-3">
        <SelectField
          aria-label="Стратегия"
          className="w-[170px]"
          value={choices.strategy ?? ''}
          onChange={(event) => set({ strategy: event.target.value })}
        >
          <option value="">Стратегия: все</option>
          {(strategies.data ?? [])
            .filter((strategy) => strategy.kind !== 'bot')
            .map((strategy) => (
              <option key={strategy.name} value={strategy.name}>
                {strategy.name}
              </option>
            ))}
        </SelectField>
        <SelectField
          aria-label="Таймфрейм"
          className="w-[100px]"
          value={choices.timeframe ?? ''}
          onChange={(event) => set({ timeframe: event.target.value })}
        >
          <option value="">ТФ: все</option>
          {timeframes.map((timeframe) => (
            <option key={timeframe} value={timeframe}>
              {timeframe}
            </option>
          ))}
        </SelectField>
        <SelectField
          aria-label="Сторона"
          className="w-[130px]"
          value={choices.side ?? ''}
          onChange={(event) => set({ side: event.target.value })}
        >
          <option value="">Сторона: все</option>
          <option value="long">лонг</option>
          <option value="short">шорт</option>
        </SelectField>
        <KindChoice
          kind={choices.kind}
          onChange={(kind: InstrumentKind | undefined) => set({ kind })}
        />
        <SelectField
          aria-label="Период"
          className="w-[130px]"
          value={choices.period ?? ''}
          onChange={(event) =>
            set({ period: event.target.value === '' ? undefined : Number(event.target.value) })
          }
        >
          <option value="">За всё время</option>
          {periods.map((period) => (
            <option key={period.days} value={period.days}>
              {period.words}
            </option>
          ))}
        </SelectField>
        {scores.data && (
          <span className="ml-auto font-mono text-[12px] text-muted">{total} идей</span>
        )}
      </div>
      {scores.isError && (
        <p role="alert" className="text-err">
          {scores.error.message}
        </p>
      )}
      {scores.data && rows.length === 0 && (
        <p className="rounded-[10px] border border-dashed border-line bg-panel px-6 py-10 text-center text-muted">
          Закрытых идей за период нет.
        </p>
      )}
      {rows.length > 0 && (
        <>
          <Table label="Итоги по баллу">
            <thead>
              <tr>
                <Th className="text-accent">Балл ↓</Th>
                <Th className="text-right">Идей</Th>
                <Th className="text-right">Прибыльных</Th>
                <Th className="text-right">Средний итог</Th>
                <Th className="text-right">Сумма, USDT</Th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <ScoreRow key={String(row.score)} row={row} />
              ))}
            </tbody>
          </Table>
          <p className="text-[12px] text-muted">
            Строка с числом идей меньше {smallSample} — малая выборка: по ней веса не подбирают.
          </p>
        </>
      )}
    </section>
  );
}

function ScoreRow({ row }: { readonly row: IdeaScore }) {
  const small = Number(row.ideas) < smallSample;

  return (
    <Tr className="h-10 font-mono" aria-label={`Балл ${scoreText(row.score)}`}>
      <Td>
        <span className="flex items-center gap-3">
          <span className={scoreTone(row.score)}>{scoreText(row.score)}</span>
          {small && (
            <Badge tone="warn" className="font-sans">
              мало идей &lt; {smallSample}
            </Badge>
          )}
        </span>
      </Td>
      <Td className="text-right">{Number(row.ideas).toLocaleString('ru-RU')}</Td>
      <Td className="text-right">{Math.round(Number(row.wins) * 100)} %</Td>
      <Td className={`text-right ${signTone(row.mean)}`}>{changeText(row.mean)}</Td>
      <Td className={`text-right ${signTone(row.sum)}`}>{signedAmount(row.sum)}</Td>
    </Tr>
  );
}
