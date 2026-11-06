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
 * «Калибровка»: what the buckets promised against how the cases they had not seen ended, over 90 or the last 30 days and by
 * category; the window and the category are in the address.
 */
export function IdeasCalibrationPage({
  days,
  category,
}: {
  days: 90 | 30;
  category: string | null;
}) {
  const navigate = useNavigate();
  const calibration = useQuery(calibrationQuery(days, category));
  const go = (next: { days?: 90 | 30; category?: string | null }) => {
    const chosen = { days, category, ...next };

    void navigate({
      to: '/ideas/calibration',
      search: {
        ...(chosen.days === 30 ? { days: 30 as const } : {}),
        ...(chosen.category === null ? {} : { category: chosen.category as 'linear' | 'spot' }),
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
          пересчитывает калибровку вместе с корзинами.
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
        Калибровка ещё не посчитана: сканер считает её вместе с корзинами раз в 10 минут.
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
      <ProbabilityTable rows={report.probabilities} />
      <ExpectancyTable rows={report.expectancies} />
      <BadgeTable rows={report.badges} checked={Number(report.checked)} />
      <ul className="space-y-1 text-xs text-faint">
        <li>
          Обещали: статистика корзины по кейсам, закрытым до срабатывания кейса и сработавшим не
          раньше чем за 90 дней до него. Кейс корзины, где таких меньше 30, не проверяется.
        </li>
        <li>
          Без корзин: каждому кейсу обещана доля «Цель» его категории за то же окно. Чем ниже оценка
          Брайера, тем лучше.
        </li>
        <li>
          Разница серая, когда обещанное внутри интервала 95 %, жёлтая за ним, красная дальше двух
          его полуширин.
        </li>
        <li>Интервалы считают кейсы независимыми, поэтому на деле они шире.</li>
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
