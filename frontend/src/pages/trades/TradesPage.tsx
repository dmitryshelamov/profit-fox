import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { riskStateKey } from '@/api/risk';
import { riskRefreshMs } from '@/api/risk';
import { closeTrade, listTrades, resolveTrade, type Trade, tradesKey } from '@/api/trades';
import { HaltBanner, RiskStrip } from '@/pages/risk/RiskStrip';
import { useRiskState } from '@/pages/risk/useRiskState';
import { localClock } from '@/shared/time';
import { Button, buttonLook } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { Page, PageTitle } from '@/shared/ui/Page';
import { PagedTable } from '@/shared/ui/PagedTable';
import { Pager } from '@/shared/ui/Pager';
import { Tabs } from '@/shared/ui/Tabs';
import { useListSettings } from '@/shared/ui/useListSettings';
import { useNow } from '@/shared/ui/useNow';

import { amountWords, signedUsdt } from './format';
import { closedTradeColumns, openTradeColumns } from './tradeColumns';

export type TradesTab = 'open' | 'closed';

/**
 * «Сделки» (change take-idea-trades, spec idea-trades «Раздел „Сделки“», «Связь с биржей на экране», «Закрытие вручную»,
 * «Ошибку разбирает владелец»; spec trade-risk «Риск в „Сделках“»): the strip of the risk, the open and the closed trades
 * a page at a time, a row opens the page of its pair. The open ones are read again every 5 s.
 */
export function TradesPage({
  tab,
  page,
  onPlace,
}: {
  readonly tab: TradesTab;
  readonly page: number;
  readonly onPlace: (tab: TradesTab, page: number) => void;
}) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const now = useNow(15_000);
  const risk = useRiskState();
  const openList = useListSettings('trades-open', { field: 'created', descending: true });
  const closedList = useListSettings('trades-closed', { field: 'exitTime', descending: true });
  const open = useQuery({
    queryKey: [...tradesKey, 'open', openList.sort, tab === 'open' ? page : 1, openList.pageSize],
    queryFn: () =>
      listTrades('open', {
        sort: openList.sort,
        page: tab === 'open' ? page : 1,
        pageSize: openList.pageSize,
      }),
    placeholderData: keepPreviousData,
    refetchInterval: riskRefreshMs,
  });
  const closed = useQuery({
    queryKey: [...tradesKey, 'closed', closedList.sort, page, closedList.pageSize],
    queryFn: () =>
      listTrades('closed', { sort: closedList.sort, page, pageSize: closedList.pageSize }),
    placeholderData: keepPreviousData,
    enabled: tab === 'closed',
  });
  const counts = (tab === 'open' ? open.data : closed.data) ?? open.data ?? closed.data;
  const none = counts !== undefined && Number(counts.open) === 0 && Number(counts.closed) === 0;

  const [asked, setAsked] = useState<Trade | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const refresh = () => {
    void client.invalidateQueries({ queryKey: tradesKey });
    void client.invalidateQueries({ queryKey: riskStateKey });
  };
  const [unchecked, setUnchecked] = useState<{ trade: Trade; reason: string } | null>(null);
  const resolve = useMutation({
    mutationFn: ({ trade, skipCheck }: { trade: Trade; skipCheck: boolean }) =>
      resolveTrade(trade.id, skipCheck),
    onSuccess: (result, { trade }) => {
      setUnchecked(null);
      if (result.ok) {
        setRefusal(null);
        refresh();
      } else if (result.canSkipCheck) {
        // Bybit cannot be asked about the position: the owner is asked instead (review R1).
        setRefusal(null);
        setUnchecked({ trade, reason: result.reason });
      } else {
        setRefusal(`${trade.symbol}: ${result.reason}`);
      }
    },
    onError: (failure, { trade }) => setRefusal(`${trade.symbol}: ${failure.message}`),
  });
  const resolving = resolve.isPending ? resolve.variables.trade.id : null;

  const openPair = (trade: Trade) =>
    void navigate({
      to: '/market/$symbol',
      params: { symbol: trade.symbol },
      search: { timeframe: trade.timeframe, idea: trade.ideaId },
    });
  const rows = open.data?.items ?? [];
  const errored = rows.filter((trade) => trade.state === 'error' && trade.reason);
  const polled = risk.data?.polled ?? [];

  return (
    <Page>
      <PageTitle
        title="Сделки"
        hint="Реальные сделки по идеям на Bybit. Стоп и цель стоят на бирже, в срок идеи сделку закрывает стенд."
      >
        <Link to="/risk" className={buttonLook('ghost')}>
          Риск →
        </Link>
      </PageTitle>
      {risk.data?.halt.haltedAt != null && (
        <HaltBanner halt={risk.data.halt}>
          <Link to="/risk" className={buttonLook('ghost')}>
            Открыть «Риск» →
          </Link>
        </HaltBanner>
      )}
      {risk.data &&
        (risk.data.limitsSet ? (
          <RiskStrip
            state={risk.data}
            closed={
              counts === undefined
                ? undefined
                : { result: counts.closedResult, fees: counts.closedFees }
            }
          />
        ) : (
          <p
            role="status"
            className="rounded-[10px] border border-line bg-panel px-6 py-5 text-warn"
          >
            Задайте пределы в разделе{' '}
            <Link to="/risk" className="underline">
              Риск
            </Link>
            , чтобы входить в идеи.
          </p>
        ))}
      {polled.length > 0 && (
        <ul aria-label="Связь с биржей" className="flex flex-col gap-1 text-warn">
          {polled.map((account) => (
            <li key={account.id}>
              {account.name} ведётся опросом с {localClock(Number(account.since))}: поток Bybit
              оборван
            </li>
          ))}
        </ul>
      )}
      {none ? (
        <section
          aria-label="Сделок нет"
          className="flex flex-col items-center gap-3 rounded-[10px] border border-dashed border-line bg-panel px-6 py-16"
        >
          <h2 className="text-base font-bold">Сделок пока нет</h2>
          <p className="text-muted">Сделка открывается кнопкой «Войти» у идеи.</p>
          <Link to="/ideas" className={buttonLook('ghost')}>
            Идеи
          </Link>
        </section>
      ) : (
        <section aria-label="Сделки" className="flex flex-col gap-5">
          <Tabs
            label="Сделки"
            value={tab}
            className="border-b border-line"
            tabs={[
              { value: 'open', label: 'Открытые', count: counts ? Number(counts.open) : undefined },
              {
                value: 'closed',
                label: 'Закрытые',
                count: counts ? Number(counts.closed) : undefined,
              },
            ]}
            onChange={(next) => onPlace(next, 1)}
          />
          {open.isError && tab === 'open' && (
            <p role="alert" className="text-err">
              {open.error.message}
            </p>
          )}
          {closed.isError && tab === 'closed' && (
            <p role="alert" className="text-err">
              {closed.error.message}
            </p>
          )}
          {tab === 'open' && open.data && (
            <>
              <PagedTable
                label="Открытые сделки"
                columns={openTradeColumns(
                  now,
                  setAsked,
                  (trade) => resolve.mutate({ trade, skipCheck: false }),
                  resolving,
                )}
                dense
                rows={open.data.items}
                sort={openList.sort}
                rowKey={(trade) => trade.id}
                rowLabel={(trade) => `${trade.symbol} ${trade.strategy} ${trade.timeframe}`}
                onSort={(sort) => {
                  openList.setSort(sort);
                  onPlace('open', 1);
                }}
                onOpen={openPair}
              />
              {open.data.items.length === 0 && <p className="text-muted">Открытых сделок нет.</p>}
              <Pager
                total={Number(open.data.total)}
                page={page}
                pageSize={openList.pageSize}
                onPage={(next) => onPlace('open', next)}
                onPageSize={(size) => {
                  openList.setPageSize(size);
                  onPlace('open', 1);
                }}
              />
            </>
          )}
          {tab === 'closed' && closed.data && (
            <>
              <PagedTable
                label="Закрытые сделки"
                columns={closedTradeColumns}
                dense
                rows={closed.data.items}
                sort={closedList.sort}
                rowKey={(trade) => trade.id}
                rowLabel={(trade) => `${trade.symbol} ${trade.strategy} ${trade.timeframe}`}
                onSort={(sort) => {
                  closedList.setSort(sort);
                  onPlace('closed', 1);
                }}
                onOpen={openPair}
              />
              {closed.data.items.length === 0 && <p className="text-muted">Закрытых сделок нет.</p>}
              <Pager
                total={Number(closed.data.total)}
                page={page}
                pageSize={closedList.pageSize}
                onPage={(next) => onPlace('closed', next)}
                onPageSize={(size) => {
                  closedList.setPageSize(size);
                  onPlace('closed', 1);
                }}
              />
            </>
          )}
          {tab === 'open' &&
            errored.map((trade) => (
              <p key={trade.id} role="alert" className="text-err">
                Ошибка · {trade.symbol}: {trade.reason}
              </p>
            ))}
          {refusal !== null && (
            <p role="alert" className="text-err">
              {refusal}
            </p>
          )}
        </section>
      )}
      {unchecked !== null && (
        <Dialog
          label="Позиция на Bybit закрыта?"
          title="Позиция на Bybit закрыта?"
          onClose={() => setUnchecked(null)}
        >
          <p className="text-muted">{unchecked.reason}</p>
          <p className="text-muted">
            Стенд не может спросить Bybit о позиции {unchecked.trade.symbol}. Если вы закрыли её на
            Bybit, сделка закроется «на бирже» без проверки, итог — по исполнениям, которые стенд
            нашёл.
          </p>
          <span className="flex justify-end gap-2">
            <Button onClick={() => setUnchecked(null)}>Не разбирать</Button>
            <Button
              variant="danger"
              className="border border-err"
              disabled={resolve.isPending}
              onClick={() => resolve.mutate({ trade: unchecked.trade, skipCheck: true })}
            >
              Позиция закрыта — разобрать
            </Button>
          </span>
        </Dialog>
      )}
      {asked !== null && (
        <CloseDialog
          trade={asked}
          onClose={() => setAsked(null)}
          onDone={(reason) => {
            setAsked(null);
            setRefusal(reason === null ? null : `${asked.symbol}: ${reason}`);
            refresh();
          }}
        />
      )}
    </Page>
  );
}

/** The question before «Закрыть по рынку» (spec idea-trades «Закрытие вручную»): it names the pair, side, amount, result. */
function CloseDialog({
  trade,
  onClose,
  onDone,
}: {
  readonly trade: Trade;
  readonly onClose: () => void;
  readonly onDone: (refusal: string | null) => void;
}) {
  const close = useMutation({
    mutationFn: () => closeTrade(trade.id),
    onSuccess: (result) => onDone(result.ok ? null : result.reason),
    onError: (failure) => onDone(failure.message),
  });
  const side = trade.side === 'short' ? 'Шорт' : 'Лонг';
  return (
    <Dialog
      label={`Закрыть ${trade.symbol} по рынку?`}
      title={`Закрыть ${trade.symbol} по рынку?`}
      onClose={onClose}
    >
      <p className="text-muted">
        {side} на {amountWords(trade.amount)} USDT, сейчас {signedUsdt(trade.unrealised)} USDT.
        Стенд {trade.side === 'short' ? 'купит' : 'продаст'} всю позицию по рынку; стоп и цель
        снимет биржа.
      </p>
      <span className="flex justify-end gap-2">
        <Button onClick={onClose}>Не закрывать</Button>
        <Button
          variant="danger"
          className="border border-err"
          disabled={close.isPending}
          onClick={() => close.mutate()}
        >
          Закрыть по рынку
        </Button>
      </span>
    </Dialog>
  );
}
