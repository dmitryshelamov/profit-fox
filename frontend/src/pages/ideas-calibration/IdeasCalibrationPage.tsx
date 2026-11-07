import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import {
  badgeSymbol,
  brierView,
  calibrationQuery,
  casesText,
  differenceView,
  rangeLabel,
} from '@/modules/ideas';
import type { Schemas } from '@/shared/api/client';
import { shiftDecimal } from '@/shared/format/decimal';
import { formatMoment, groupDigits, roundDecimal, showDecimal } from '@/shared/format/format';

type Report = Schemas['IdeaCalibrationResponse'];
type Row = Schemas['CalibrationRowResponse'];
type Own = Schemas['CalibrationOwnRowResponse'];
type Comparison = Schemas['CalibrationComparisonResponse'];
type Scope = 'all' | 'fresh';

const cell = 'px-3 py-2';
const number = `${cell} text-right font-mono whitespace-nowrap`;
const switchButton =
  'h-8 px-3 text-sm text-muted first:rounded-l-lg last:rounded-r-lg hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text';
const toneColour = { muted: 'text-muted', warn: 'text-warn', err: 'text-err' } as const;

const windows = [
  { days: 90, label: 'Кейсы 90 дн' },
  { days: 30, label: 'за 30 дн' },
] as const;

const categories = [
  { category: null, label: 'Все' },
  { category: 'linear', label: 'linear' },
  { category: 'spot', label: 'spot' },
] as const;

const scopes = [
  { scope: 'all', label: 'Все случаи' },
  { scope: 'fresh', label: 'После изменения правил' },
] as const;

const percent = (share: string) => `${roundDecimal(shiftDecimal(share, 2), 0)} %`;
const inR = (value: string) => `${showDecimal(value, 2, true)} R`;

function shareInterval(low: string | null, high: string | null): string {
  return low === null || high === null
    ? '—'
    : `${roundDecimal(shiftDecimal(low, 2), 0)}–${roundDecimal(shiftDecimal(high, 2), 0)} %`;
}

function resultInterval(low: string | null, high: string | null): string {
  return low === null || high === null
    ? '—'
    : `${showDecimal(low, 2, true)}…${showDecimal(high, 2, true)} R`;
}

/**
 * «Калибровка»: what the model of the buckets promised against how the cases it had not seen ended, over 90 or the last 30 days, by
 * category, among all cases or those after the rules changed; the window, the category and the scope are in the address.
 */
export function IdeasCalibrationPage({
  days,
  category,
  scope = 'all',
}: {
  days: 90 | 30;
  category: string | null;
  scope?: Scope;
}) {
  const navigate = useNavigate();
  const calibration = useQuery(calibrationQuery(days, category, scope));
  const go = (next: { days?: 90 | 30; category?: string | null; scope?: Scope }) => {
    const chosen = { days, category, scope, ...next };

    void navigate({
      to: '/ideas/calibration',
      search: {
        ...(chosen.days === 30 ? { days: 30 as const } : {}),
        ...(chosen.category === null ? {} : { category: chosen.category as 'linear' | 'spot' }),
        ...(chosen.scope === 'fresh' ? { scope: 'fresh' as const } : {}),
      },
    });
  };

  return (
    <section className="space-y-5">
      <nav aria-label="Путь" className="text-sm text-muted">
        <Link to="/ideas" className="hover:text-text">
          Идеи
        </Link>{' '}
        / <span className="text-text">Калибровка</span>
      </nav>
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Калибровка</h1>
        <p className="text-sm text-muted">
          Что обещали корзины и как закончились кейсы, которых они ещё не видели. Сканер
          пересчитывает калибровку вместе с корзинами.{' '}
          <Link to="/ideas/exit-rules" className="text-run hover:underline">
            Срок жизни и цель
          </Link>
        </p>
      </header>
      <div className="flex flex-wrap items-center gap-3">
        <div role="group" aria-label="Окно" className="flex rounded-lg border border-line">
          {windows.map((window) => (
            <button
              key={window.days}
              type="button"
              aria-pressed={days === window.days}
              onClick={() => go({ days: window.days })}
              className={switchButton}
            >
              {window.label}
            </button>
          ))}
        </div>
        <div role="group" aria-label="Категория" className="flex rounded-lg border border-line">
          {categories.map((choice) => (
            <button
              key={choice.label}
              type="button"
              aria-pressed={category === choice.category}
              onClick={() => go({ category: choice.category })}
              className={switchButton}
            >
              {choice.label}
            </button>
          ))}
        </div>
        <div role="group" aria-label="Режим" className="flex rounded-lg border border-line">
          {scopes.map((choice) => (
            <button
              key={choice.scope}
              type="button"
              aria-pressed={scope === choice.scope}
              onClick={() => go({ scope: choice.scope })}
              className={switchButton}
            >
              {choice.label}
            </button>
          ))}
        </div>
      </div>
      {calibration.isError && <p role="alert">Не удалось загрузить калибровку.</p>}
      {calibration.isPending && <p className="text-sm text-muted">Загрузка калибровки…</p>}
      {calibration.data && <ReportView report={calibration.data} />}
    </section>
  );
}

function ReportView({ report }: { report: Report }) {
  if (report.computedAt === null) {
    return (
      <p className="text-sm text-muted">
        Калибровка ещё не посчитана: сканер считает её вместе с корзинами раз в час.
      </p>
    );
  }

  const counters = `Проверено ${casesText(Number(report.checked))} · без статистики корзины ${groupDigits(String(report.unchecked))} · посчитано ${formatMoment(report.computedAt)} UTC`;

  if (report.total === null) {
    return (
      <>
        <p className="text-sm text-muted">{counters}</p>
        <p className="text-sm text-muted">Проверенных кейсов нет.</p>
      </>
    );
  }

  return (
    <>
      <p className="text-sm text-muted">{counters}</p>
      <Tiles report={report} total={report.total} />
      {report.comparison !== null && <ComparisonTable comparison={report.comparison} />}
      <ProbabilityTable rows={report.probabilities} />
      <ExpectancyTable rows={report.expectancies} />
      <BadgeTable rows={report.badges} checked={Number(report.checked)} />
      <OwnTable rows={report.owns} />
      <ul className="space-y-1 text-xs text-faint">
        <li>
          Обещали: числа модели корзины по кейсам, закрытым до срабатывания кейса и сработавшим не
          раньше чем за 90 дней до него, — корзины, её родителя и категории. Кейс, у родителя
          корзины которого таких меньше 30, не проверяется.
        </li>
        <li>
          Старая модель: собственные числа корзины без скученности и подтягивания, от 30 своих
          кейсов; сравнение — на кейсах, которые проверяют обе модели.
        </li>
        <li>
          После изменения правил: только кейсы, сработавшие после применения правила выхода своего
          типа × ТФ и после начала текущей версии правил идей.
        </li>
        <li>
          Без корзин: каждому кейсу обещана доля «Цель» его категории за то же окно. Чем ниже оценка
          Брайера, тем лучше.
        </li>
        <li>
          Разница серая, когда обещанное внутри интервала 95 %, жёлтая за ним, красная дальше двух
          его полуширин.
        </li>
        <li>Интервалы считают кейсы, сработавшие в один час, одной группой.</li>
      </ul>
    </>
  );
}

function Tile({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <section
      aria-label={label}
      className="space-y-1 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <h2 className="text-[11px] tracking-wide text-faint uppercase">{label}</h2>
      <p className="font-mono text-lg">{value}</p>
      <p className="text-sm text-muted">{note}</p>
    </section>
  );
}

function Tiles({ report, total }: { report: Report; total: Row }) {
  const brier = brierView(report.brier!, report.baseBrier!);
  const up = report.badges.find((row) => row.badge === 'up');

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <Tile label="Оценка Брайера" value={brier.scores} note={brier.verdict} />
      <Tile
        label="▲ на деле"
        value={up === undefined ? '—' : inR(up.expectancy)}
        note={
          up === undefined
            ? 'кейсов со значком ▲ нет'
            : `${resultInterval(up.expectancyLow, up.expectancyHigh)} · ${casesText(Number(up.cases))}`
        }
      />
      <Tile
        label="Ожидание"
        value={`${inR(total.promised)} → ${inR(total.expectancy)}`}
        note="обещали → получили, все кейсы"
      />
    </div>
  );
}

function Table({
  label,
  headers,
  children,
}: {
  label: string;
  headers: string[];
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-semibold">{label}</h2>
      <div className="overflow-x-auto">
        <table aria-label={label} className="w-full text-sm">
          <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
            <tr>
              {headers.map((header, index) => (
                <th key={header} scope="col" className={index === 0 ? cell : `${cell} text-right`}>
                  {header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">{children}</tbody>
        </table>
      </div>
    </section>
  );
}

function ProbabilityTable({ rows }: { rows: readonly Row[] }) {
  return (
    <Table
      label="Вероятность"
      headers={[
        'Обещали',
        'Кейсов',
        'Обещали, ср.',
        'Сбылось',
        'Разница',
        'Интервал 95 %',
        'Стоп',
        'Истекла',
      ]}
    >
      {rows.map((row) => {
        const difference = differenceView({
          promised: row.promised,
          actual: row.targetShare,
          low: row.probabilityLow,
          high: row.probabilityHigh,
        });

        return (
          <tr key={row.from ?? 'low'}>
            <td className={cell}>{rangeLabel(row.from, row.to, '%')}</td>
            <td className={number}>{groupDigits(String(row.cases))}</td>
            <td className={number}>{percent(row.promised)}</td>
            <td className={number}>{percent(row.targetShare)}</td>
            <td className={number}>
              <span className={toneColour[difference.tone]}>{difference.text}</span>
            </td>
            <td className={number}>{shareInterval(row.probabilityLow, row.probabilityHigh)}</td>
            <td className={number}>{percent(row.stopShare)}</td>
            <td className={number}>{percent(row.expiredShare)}</td>
          </tr>
        );
      })}
    </Table>
  );
}

function ExpectancyTable({ rows }: { rows: readonly Row[] }) {
  return (
    <Table label="Ожидание" headers={['Обещали', 'Кейсов', 'Обещали, ср.', 'Получили']}>
      {rows.map((row) => (
        <tr key={row.from ?? 'low'}>
          <td className={cell}>{rangeLabel(row.from, row.to, 'R')}</td>
          <td className={number}>{groupDigits(String(row.cases))}</td>
          <td className={number}>{inR(row.promised)}</td>
          <td className={number}>{inR(row.expectancy)}</td>
        </tr>
      ))}
    </Table>
  );
}

function BadgeTable({ rows, checked }: { rows: readonly Row[]; checked: number }) {
  return (
    <Table
      label="Значки"
      headers={['Значок', 'Кейсов', 'Доля', 'Обещали', 'Получили', 'Интервал 95 %', 'Целей']}
    >
      {rows.map((row) => (
        <tr key={row.badge}>
          <td className={cell}>{badgeSymbol(row.badge!)}</td>
          <td className={number}>{groupDigits(String(row.cases))}</td>
          <td className={number}>{Math.round((Number(row.cases) * 100) / checked)} %</td>
          <td className={number}>{inR(row.promised)}</td>
          <td className={number}>{inR(row.expectancy)}</td>
          <td className={number}>{resultInterval(row.expectancyLow, row.expectancyHigh)}</td>
          <td className={number}>{percent(row.targetShare)}</td>
        </tr>
      ))}
    </Table>
  );
}

function ComparisonTable({ comparison }: { comparison: Comparison }) {
  const models = [
    { label: 'Старая', model: comparison.old },
    { label: 'Новая', model: comparison.new },
    ...(comparison.features === null
      ? []
      : [{ label: 'С признаками', model: comparison.features }]),
  ];

  return (
    <Table
      label="Старая модель → новая"
      headers={['Модель', 'Случаев', 'Брайер', 'С ▲', 'Обещали', 'Получили', 'Интервал 95 %']}
    >
      {models.map(({ label, model }) => (
        <tr key={label}>
          <td className={cell}>{label}</td>
          <td className={number}>{groupDigits(String(model.cases))}</td>
          <td className={number}>{model.brier === null ? '—' : roundDecimal(model.brier, 3)}</td>
          <td className={number}>{groupDigits(String(model.up?.cases ?? 0))}</td>
          <td className={number}>{model.up === null ? '—' : inR(model.up.promised)}</td>
          <td className={number}>{model.up === null ? '—' : inR(model.up.expectancy)}</td>
          <td className={number}>
            {model.up === null
              ? '—'
              : resultInterval(model.up.expectancyLow, model.up.expectancyHigh)}
          </td>
        </tr>
      ))}
    </Table>
  );
}

function OwnTable({ rows }: { rows: readonly Own[] }) {
  return (
    <Table
      label="По своим данным"
      headers={['Свои данные', 'Кейсов', 'Обещали', 'Получили', 'Интервал 95 %', 'Целей', 'Брайер']}
    >
      {rows.map((row) => (
        <tr key={String(row.fewOwnCases)}>
          <td className={cell}>{row.fewOwnCases ? 'мало своих данных' : '30 и больше своих'}</td>
          <td className={number}>{groupDigits(String(row.cases))}</td>
          <td className={number}>{inR(row.promised)}</td>
          <td className={number}>{inR(row.expectancy)}</td>
          <td className={number}>{resultInterval(row.expectancyLow, row.expectancyHigh)}</td>
          <td className={number}>{percent(row.targetShare)}</td>
          <td className={number}>{roundDecimal(row.brier, 3)}</td>
        </tr>
      ))}
    </Table>
  );
}
