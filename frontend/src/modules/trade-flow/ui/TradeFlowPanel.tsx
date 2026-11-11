import { feedText } from '@/modules/bots';
import { compactNumber } from '@/modules/market';
import { TradeFlowChart } from '@/modules/market-chart';
import { intervalLabel } from '@/modules/market-data';
import { formatClock, formatMoment } from '@/shared/format/format';

import { barCaption } from '../model/caption';
import { useTradeFlow } from '../model/useTradeFlow';

/**
 * The panel «Покупки и продажи» under the candles of a pair of linear or spot, on the interval of the chart: the bars of buys and sells with the
 * line of CVD, the caption of the last bar, the state of the channel of the current bar, where the data of the pair start and the gaps of the flow.
 * A failure of the bars stays inside the panel.
 */
export function TradeFlowPanel({
  category,
  symbol,
  interval,
}: {
  category: string;
  symbol: string;
  interval: string;
}) {
  const { query, feed, shown } = useTradeFlow(category, symbol, interval);
  const last = shown?.bars.at(-1);
  const caption = last === undefined ? null : barCaption(last);
  const data = query.data;

  return (
    <section
      aria-label="Покупки и продажи"
      className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-semibold">
          Покупки и продажи{' '}
          <span className="ml-2 font-normal text-faint">USDT · CVD за период</span>
        </h2>
        <span className="text-xs text-muted">{feedText(feed)}</span>
      </div>
      {caption && (
        <div
          role="group"
          aria-label="Последний бар"
          className="flex flex-wrap gap-x-6 font-mono text-xs text-muted"
        >
          <span>Покупки {caption.buys}</span>
          <span>Продажи {caption.sells}</span>
          <span>Разность {caption.difference}</span>
          {shown?.incomplete && <span className="text-warn">неполный</span>}
        </div>
      )}
      {query.isError && <p role="alert">Данные потока сделок недоступны.</p>}
      {data && (
        <>
          <TradeFlowChart
            bars={shown?.bars ?? []}
            current={shown?.current ?? null}
            label={`Покупки и продажи ${symbol}, ${intervalLabel(interval)}`}
            formatValue={(value) => compactNumber(String(Math.abs(value)))}
          />
          {data.since === null ? (
            <p className="text-sm text-muted">данных пока нет</p>
          ) : (
            Date.parse(data.since) > Date.parse(data.from) && (
              <p className="text-sm text-muted">данных нет до {formatMoment(data.since)}</p>
            )
          )}
          {data.gaps.map((gap) => (
            <p key={gap.from} className="text-sm text-warn">
              Разрыв {formatMoment(gap.from)}–{formatClock(gap.to)}
            </p>
          ))}
        </>
      )}
    </section>
  );
}
