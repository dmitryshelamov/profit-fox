import type { Schemas } from '@/shared/api/client';
import { groupDigits } from '@/shared/format/format';

const cell = 'px-4 py-2';

/** The coins or shares of an account with their value in the currency of the balance: USD on Bybit, RUB on T-Invest. */
export function BalanceTable({ balance }: { balance: Schemas['AccountBalanceResponse'] }) {
  return (
    <table aria-label="Баланс" className="w-full text-sm">
      <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
        <tr>
          <th className={cell}>{balance.currency === 'RUB' ? 'Бумага' : 'Монета'}</th>
          <th className={`${cell} text-right`}>
            {balance.currency === 'RUB' ? 'Количество' : 'Баланс'}
          </th>
          <th className={`${cell} text-right`}>Стоимость, {balance.currency}</th>
        </tr>
      </thead>
      <tbody>
        {balance.coins.map((coin) => (
          <tr key={coin.coin} className="border-t border-line">
            <td className={`${cell} font-mono font-semibold`}>{coin.coin}</td>
            <td className={`${cell} text-right font-mono`}>{groupDigits(coin.walletBalance)}</td>
            <td className={`${cell} text-right font-mono`}>{groupDigits(coin.value)}</td>
          </tr>
        ))}
      </tbody>
      <tfoot>
        <tr className="border-t border-line font-semibold">
          <td className={cell}>
            {balance.currency === 'RUB' ? 'Общая стоимость' : 'Общий капитал'}
          </td>
          <td className={cell} />
          <td className={`${cell} text-right font-mono`}>{groupDigits(balance.totalEquity)}</td>
        </tr>
      </tfoot>
    </table>
  );
}
