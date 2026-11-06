import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { accountsQuery } from '@/modules/exchange-accounts';
import { type Idea, sideLabel, typeLabel } from '@/modules/ideas';
import { ApiError } from '@/shared/api/client';
import { showDecimal } from '@/shared/format/format';
import { useDebounced } from '@/shared/hooks/useDebounced';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import { ideaEntryQuery, tradingSettingsQuery, useEnterIdea } from '../api';
import { reasonText } from '../model/reasons';

const control = 'h-9 w-full rounded-lg border border-line bg-panel px-2 text-sm';

function Line({ label, children }: { label: string; children: string }) {
  return (
    <div className="flex justify-between gap-4 text-sm">
      <dt className="text-muted">{label}</dt>
      <dd className="font-mono">{children}</dd>
    </div>
  );
}

function EntryForm({
  idea,
  initialAccount,
  initialAmount,
  onEntered,
  onClose,
}: {
  idea: Idea;
  initialAccount: string;
  initialAmount: string;
  onEntered: () => void;
  onClose: () => void;
}) {
  const [accountId, setAccountId] = useState(initialAccount);
  const [amount, setAmount] = useState(initialAmount);
  const accounts = useQuery(accountsQuery);
  // The amount is asked about a moment after it was typed, not on every key.
  const asked = useDebounced(amount, 300);
  const entry = useQuery(ideaEntryQuery(idea.id, accountId === '' ? null : accountId, asked));
  const enter = useEnterIdea();
  const calculation = entry.data;
  const refused = enter.error instanceof ApiError ? enter.error.reasons : [];
  const reasons = calculation?.reasons ?? [];

  return (
    <Dialog
      title="Вход по идее"
      onClose={onClose}
      width={560}
      actions={
        <>
          <Button look="ghost" onClick={onClose}>
            Не входить
          </Button>
          <Button
            look="primary"
            disabled={calculation === undefined || reasons.length > 0 || enter.isPending}
            onClick={() =>
              enter.mutate(
                { ideaId: idea.id, accountId: accountId === '' ? null : accountId, amount },
                { onSuccess: onEntered },
              )
            }
          >
            Войти
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted">
        {idea.symbol} · {typeLabel(idea.type)} · {sideLabel(idea.side)}. Стоп {idea.stop}, цель{' '}
        {idea.target}.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <label className="block space-y-1 text-sm">
          <span className="text-muted">Аккаунт</span>
          <select
            value={accountId}
            onChange={(event) => setAccountId(event.target.value)}
            className={control}
          >
            <option value="">не выбран</option>
            {accounts.data?.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </select>
        </label>
        <label className="block space-y-1 text-sm">
          <span className="text-muted">Сумма, USDT</span>
          <input
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            className={`${control} font-mono`}
          />
        </label>
      </div>
      {entry.isPending && <p className="text-sm text-muted">Считаем вход…</p>}
      {entry.isError && (
        <p role="alert" className="text-sm text-err">
          {entry.error instanceof ApiError && entry.error.errors.amount
            ? entry.error.errors.amount.join(' ')
            : 'Не удалось посчитать вход.'}
        </p>
      )}
      {calculation && (
        <dl className="space-y-1.5 rounded-lg border border-line bg-panel-2 px-3 py-3">
          <Line label="Цена входа">{calculation.entryPrice}</Line>
          <Line label="Объём">{calculation.quantity}</Line>
          <Line label="Сумма">{`${showDecimal(calculation.amount, 2)} ${calculation.quote}`}</Line>
          <Line label="Убыток по стопу">{`${showDecimal(calculation.risk, 2)} ${calculation.quote}`}</Line>
          <Line label="Прибыль по цели">{`${showDecimal(calculation.profit, 2)} ${calculation.quote}`}</Line>
          <Line label="R:R после комиссий">
            {`${showDecimal(calculation.riskReward, 2)} (без ком. ${showDecimal(calculation.riskRewardWithoutFees, 2)})`}
          </Line>
          <Line label="Свободные деньги">
            {calculation.freeMoney === null
              ? '—'
              : `${showDecimal(calculation.freeMoney, 0)} ${calculation.quote}`}
          </Line>
        </dl>
      )}
      {reasons.length > 0 && (
        <ul aria-label="Причины отказа" className="space-y-1 text-sm text-err">
          {reasons.map((reason) => (
            <li key={reason}>{reasonText(reason)}</li>
          ))}
        </ul>
      )}
      {enter.error && (
        <ul aria-label="Отказ при входе" role="alert" className="space-y-1 text-sm text-err">
          {refused.length > 0 ? (
            refused.map((reason) => <li key={reason}>{reasonText(reason)}</li>)
          ) : (
            <li>{enter.error instanceof ApiError ? enter.error.message : 'Не удалось войти.'}</li>
          )}
        </ul>
      )}
    </Dialog>
  );
}

/**
 * The window of an entry by an idea: the account and the amount, by default those of the settings, and the calculation of the API for
 * them, read again when they change and at least every 5 seconds. The entry is off while there is a reason, which is named. «Войти»
 * saves the trade and calls `onEntered`.
 */
export function EntryDialog({
  idea,
  onEntered,
  onClose,
}: {
  idea: Idea;
  onEntered: () => void;
  onClose: () => void;
}) {
  const settings = useQuery(tradingSettingsQuery());

  if (!settings.data) {
    return (
      <Dialog
        title="Вход по идее"
        onClose={onClose}
        actions={
          <Button look="ghost" onClick={onClose}>
            Не входить
          </Button>
        }
      >
        {settings.isError ? (
          <p role="alert" className="text-sm text-err">
            Не удалось загрузить настройки.
          </p>
        ) : (
          <p className="text-sm text-muted">Загрузка…</p>
        )}
      </Dialog>
    );
  }

  return (
    <EntryForm
      idea={idea}
      initialAccount={settings.data.accountId ?? ''}
      initialAmount={settings.data.amount}
      onEntered={onEntered}
      onClose={onClose}
    />
  );
}
