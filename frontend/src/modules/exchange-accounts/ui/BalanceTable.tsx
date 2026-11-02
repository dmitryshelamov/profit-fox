import type { Schemas } from '@/shared/api/client';
import { groupDigits } from '@/shared/format/format';

const cell = 'px-4 py-2';

export function BalanceTable({ balance }: { balance: Schemas['AccountBalanceResponse'] }) {
  return (
    <table aria-label="Баланс" className="w-full text-sm">
      <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
        <tr>
          <th className={cell}>Монета</th>
          <th className={`${cell} text-right`}>Баланс</th>
          <th className={`${cell} text-right`}>Стоимость, USD</th>
        </tr>
      </thead>
      <tbody>
        {balance.coins.map((coin) => (
          <tr key={coin.coin} className="border-t border-line">
            <td className={`${cell} font-mono font-semibold`}>{coin.coin}</td>
            <td className={`${cell} text-right font-mono`}>{groupDigits(coin.walletBalance)}</td>
            <td className={`${cell} text-right font-mono`}>{groupDigits(coin.usdValue)}</td>
          </tr>
        ))}
      </tbody>
      <tfoot>
        <tr className="border-t border-line font-semibold">
          <td className={cell}>Общий капитал</td>
          <td className={cell} />
          <td className={`${cell} text-right font-mono`}>{groupDigits(balance.totalEquityUsd)}</td>
        </tr>
      </tfoot>
    </table>
  );
}
