import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { accountsKey, freeText, listAccounts } from '@/api/accounts';
import type { Idea } from '@/api/ideas';
import { riskRefreshMs, riskStateKey } from '@/api/risk';
import { scoreText } from '@/api/scoring';
import { type EntryPreview, openTrade, previewEntry, tradesKey } from '@/api/trades';
import { changeText, momentText, priceWords, signTone } from '@/pages/market/format';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { amountWords, plainPercent, signedUsdt } from './format';

/** The checks shown in the left column, in this order; every other check, a failed one whatever its code, in the right. */
const leftChecks = ['amount', 'total', 'position', 'margin'];

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

const parseAmount = (text: string): number | null => {
  const number = Number(text.trim().replace(',', '.'));
  return text.trim() !== '' && Number.isFinite(number) && number > 0 ? number : null;
};

const percentOf = (price: number, level: number) =>
  plainPercent((Math.abs(level - price) / price) * 100);

function Row({ label, children }: { readonly label: string; readonly children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[170px_1fr] gap-3 py-1">
      <dt className="text-muted">{label}</dt>
      <dd className="font-mono">{children}</dd>
    </div>
  );
}

function Checks({ preview }: { readonly preview: EntryPreview }) {
  const left = preview.checks.filter((check) => leftChecks.includes(check.code));
  const right = preview.checks.filter((check) => !leftChecks.includes(check.code));
  const column = (label: string, items: typeof left) => (
    <ul aria-label={label} className="flex flex-col gap-1.5">
      {items.map((check) => (
        <li
          key={check.code}
          data-code={check.code}
          data-passed={check.passed}
          className={`flex gap-2 text-xs ${check.passed ? 'text-muted' : 'text-err'}`}
        >
          <span aria-hidden="true" className={check.passed ? 'text-ok' : 'text-err'}>
            {check.passed ? '✓' : '✕'}
          </span>
          <span>
            <span className="sr-only">{check.passed ? 'Пройдена: ' : 'Не пройдена: '}</span>
            {check.text}
          </span>
        </li>
      ))}
    </ul>
  );

  return (
    <div className="grid grid-cols-2 gap-x-6 border-t border-line pt-4">
      {column('Проверки слева', left)}
      {column('Проверки справа', right)}
    </div>
  );
}

function Count({ preview, idea }: { readonly preview: EntryPreview; readonly idea: Idea }) {
  const price = Number(preview.price);
  const coin = idea.symbol.replace(/USDT$/, '');
  const amount = Number(preview.amount);
  const share = (value: number) =>
    amount > 0 ? plainPercent((Math.abs(value) / amount) * 100) : '0';
  const protective =
    preview.stopSource === 'idea'
      ? `стоп идеи ${priceWords(preview.stop)} (предел ${percentOf(price, Number(preview.limitStop))} % — ${priceWords(preview.limitStop)})`
      : preview.ideaStop === null || preview.ideaStop === undefined
        ? `у идеи стопа нет · по пределу ${percentOf(price, Number(preview.stop))} % — ${priceWords(preview.stop)}`
        : `стоп идеи ${priceWords(preview.ideaStop)} дальше предела · по пределу ${percentOf(price, Number(preview.stop))} % — ${priceWords(preview.stop)}`;
  const toward = (value: number | string | null | undefined) =>
    value === null || value === undefined ? '—' : changeText(value);

  return (
    <>
      <div className="rounded-md border border-line bg-panel-2 p-4">
        <table aria-label="Уровни входа" className="w-full text-left text-[13px]">
          <thead className="text-[11px] text-muted uppercase">
            <tr>
              <th className="font-semibold" />
              <th className="text-left font-semibold">Идея</th>
              <th className="text-left font-semibold">Сейчас</th>
              <th className="text-left font-semibold">От текущей цены</th>
            </tr>
          </thead>
          <tbody className="font-mono">
            <tr>
              <td className="font-sans text-muted">Цена входа</td>
              <td>{priceWords(preview.ideaEntry)}</td>
              <td>{priceWords(preview.price)}</td>
              <td className="text-muted">
                {changeText(price / Number(preview.ideaEntry) - 1)} к идее
              </td>
            </tr>
            <tr>
              <td className="font-sans text-muted">Стоп</td>
              <td className="text-err">{priceWords(preview.ideaStop)}</td>
              <td className="text-err">{priceWords(preview.stop)}</td>
              <td className="text-err">{toward(preview.stopDistance)}</td>
            </tr>
            <tr>
              <td className="font-sans text-muted">Цель</td>
              <td className="text-ok">{priceWords(preview.ideaTarget)}</td>
              <td className="text-ok">{priceWords(preview.target)}</td>
              <td className="text-ok">{toward(preview.targetDistance)}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <dl aria-label="Расчёт входа" className="flex flex-col border-t border-line pt-3">
        <Row label="Количество">
          {Number(preview.quantity)} {coin} ≈ {Number(preview.value).toFixed(2)} USDT
        </Row>
        <Row label="Залог">{Number(preview.margin).toFixed(2)} USDT</Row>
        <Row label="Потеря на стопе">
          {preview.lossAtStop === null || preview.lossAtStop === undefined ? (
            '—'
          ) : (
            <span className="text-err">
              −{Math.abs(Number(preview.lossAtStop)).toFixed(2)} USDT · −
              {share(Number(preview.lossAtStop))} % суммы
            </span>
          )}
        </Row>
        <Row label="Прибыль на цели">
          {preview.profitAtTarget === null || preview.profitAtTarget === undefined ? (
            '—'
          ) : (
            <span className={signTone(preview.profitAtTarget)}>
              {signedUsdt(preview.profitAtTarget)} USDT ·{' '}
              {Number(preview.profitAtTarget) < 0 ? '−' : '+'}
              {share(Number(preview.profitAtTarget))} % суммы
            </span>
          )}
        </Row>
        <Row label="Защитный стоп">
          <span className={preview.stopSource === 'limit' ? 'text-warn' : ''}>{protective}</span>
        </Row>
        <Row label="Закрытие по сроку">{momentText(preview.until)} по рынку</Row>
      </dl>
    </>
  );
}

/**
 * The entry dialog (change take-idea-trades, spec idea-trades «Диалог входа», «Расчёт перед входом»): the idea, its levels
 * against the price now, the account and the amount, the count read at Bybit every 5 s and at every change, and the checks.
 * The button names the action and the amount and waits for every check; the refusal of the server stays in the dialog.
 */
export function EntryDialog({
  idea,
  onClose,
}: {
  readonly idea: Idea;
  readonly onClose: () => void;
}) {
  const client = useQueryClient();
  const accounts = useQuery({ queryKey: accountsKey, queryFn: listAccounts });
  // Empty until the owner changes them: the server fills them from the settings of «Риск».
  const [accountId, setAccountId] = useState<string | null>(null);
  const [amountText, setAmountText] = useState<string | null>(null);
  const asked = useDebounced(amountText, 300);
  const amount = asked === null ? null : parseAmount(asked);
  const invalid = amountText !== null && parseAmount(amountText) === null;
  const preview = useQuery({
    queryKey: [...tradesKey, 'preview', idea.id, accountId, amount],
    queryFn: () => previewEntry({ ideaId: idea.id, accountId, amount }),
    enabled: !invalid,
    placeholderData: keepPreviousData,
    refetchInterval: riskRefreshMs,
    retry: false,
  });
  const data = preview.data;
  const [refusal, setRefusal] = useState<readonly string[]>([]);
  const enter = useMutation({
    mutationFn: () =>
      openTrade({
        ideaId: idea.id,
        accountId: accountId ?? data?.accountId ?? null,
        // The amount the dialog shows, as the button names it — not the default of the settings now (review R4).
        amount: parseAmount(shownAmount),
      }),
    onSuccess: (result) => {
      if (result.ok) {
        void client.invalidateQueries({ queryKey: tradesKey });
        void client.invalidateQueries({ queryKey: riskStateKey });
        void client.invalidateQueries({ queryKey: ['ideas'] });
        void client.invalidateQueries({ queryKey: ['pair-ideas'] });
        onClose();
      } else {
        setRefusal(result.reasons);
      }
    },
    onError: (failure) => setRefusal([failure.message]),
  });

  const shownAmount = amountText ?? (data ? String(Number(data.amount)) : '');
  const shownAccount = accountId ?? data?.accountId ?? '';
  const typed = parseAmount(shownAmount);
  const side = idea.side === 'short' ? 'Продать' : 'Купить';
  const ready =
    data !== undefined &&
    data.passed &&
    !preview.isPlaceholderData &&
    !invalid &&
    typed !== null &&
    amountText === asked &&
    !enter.isPending;

  return (
    <Dialog label="Вход в сделку" className="w-[700px]" onClose={onClose}>
      <div className="flex items-center justify-between">
        <h2 className="text-base font-bold">Вход в сделку</h2>
        <button
          type="button"
          aria-label="Закрыть окно"
          className="cursor-pointer text-lg leading-none text-muted hover:text-text"
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <div className="flex flex-col gap-1">
        <span className="flex items-center gap-3">
          <span className="font-mono text-[15px] font-bold">{idea.symbol}</span>
          <Badge tone={idea.side === 'short' ? 'err' : 'ok'}>
            {idea.side === 'short' ? 'шорт' : 'лонг'}
          </Badge>
        </span>
        <span className="text-xs text-muted">
          {idea.strategy} v{String(idea.version)} · {idea.timeframe} · балл {scoreText(idea.score)}{' '}
          · появилась {momentText(idea.moment)} · срок {momentText(idea.until)}
        </span>
      </div>
      {preview.isError && (
        <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
          Расчёт не получен: {preview.error.message}
        </p>
      )}
      {data && <Count preview={data} idea={idea} />}
      <div className="flex items-start gap-4">
        <label className="flex min-w-0 flex-1 flex-col gap-1.5 text-xs text-muted">
          Аккаунт
          <SelectField
            aria-label="Аккаунт"
            value={shownAccount}
            onChange={(event) => setAccountId(event.target.value)}
          >
            {shownAccount === '' && <option value="">Не выбран</option>}
            {(accounts.data ?? []).map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </SelectField>
          <span className="text-faint">
            {data?.free === null || data?.free === undefined
              ? 'свободные USDT не прочитаны'
              : `свободно ${freeText(Number(data.free))} USDT`}
          </span>
        </label>
        <label className="flex w-[150px] flex-col gap-1.5 text-xs text-muted">
          Сумма сделки
          <TextField
            aria-label="Сумма сделки"
            mono
            inputMode="decimal"
            unit="USDT"
            value={shownAmount}
            aria-invalid={invalid}
            className={invalid ? 'border-err' : ''}
            onChange={(event) => {
              setRefusal([]);
              setAmountText(event.target.value);
            }}
          />
          <span className="text-faint">стоимость позиции</span>
        </label>
        <label className="flex w-[90px] flex-col gap-1.5 text-xs text-muted">
          Плечо
          <TextField
            aria-label="Плечо"
            mono
            readOnly
            value={data ? `${String(data.leverage)}×` : ''}
          />
          <span className="text-faint">из настроек</span>
        </label>
      </div>
      {data && <Checks preview={data} />}
      {refusal.length > 0 && (
        <div
          role="alert"
          className="rounded-md border border-err bg-err-bg px-4 py-3 text-xs text-err"
        >
          {refusal.map((reason) => (
            <p key={reason}>{reason}</p>
          ))}
        </div>
      )}
      <span className="flex justify-end gap-2">
        <Button onClick={onClose}>Отмена</Button>
        <Button
          variant="primary"
          disabled={!ready}
          onClick={() => {
            setRefusal([]);
            enter.mutate();
          }}
        >
          {side} на {typed === null ? '—' : amountWords(typed)} USDT
        </Button>
      </span>
    </Dialog>
  );
}
