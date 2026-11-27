import type { InstrumentKind } from '@/api/market';
import { SelectField } from '@/shared/ui/SelectField';

import { isInstrumentKind, kindWords } from './useInstrumentKind';

/** The list «Вид: все / крипта / акции» above the tables of «Рынок», «Открытые» and «Журнал» (design.md, decision 5). */
export function KindChoice({
  kind,
  onChange,
}: {
  readonly kind: InstrumentKind | undefined;
  readonly onChange: (kind: InstrumentKind | undefined) => void;
}) {
  return (
    <SelectField
      aria-label="Вид"
      className="w-[120px]"
      value={kind ?? ''}
      onChange={(event) =>
        onChange(isInstrumentKind(event.target.value) ? event.target.value : undefined)
      }
    >
      <option value="">Вид: все</option>
      {(['crypto', 'stock'] as const).map((value) => (
        <option key={value} value={value}>
          {kindWords[value].many}
        </option>
      ))}
    </SelectField>
  );
}
