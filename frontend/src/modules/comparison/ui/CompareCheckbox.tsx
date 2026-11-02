import type { ComparisonItem } from '../model/items';
import { toggleComparison } from '../model/store';
import { useComparison } from './useComparison';

/** Marks a result for comparison; marking it again removes it. */
export function CompareCheckbox({ item }: { item: ComparisonItem }) {
  const items = useComparison();
  const marked = items.some(
    (entry) => entry.backtestId === item.backtestId && entry.combination === item.combination,
  );

  return (
    <input
      type="checkbox"
      aria-label={`Сравнить результат ${item.combination}`}
      checked={marked}
      className="size-4 accent-accent"
      onClick={(event) => event.stopPropagation()}
      onChange={() => toggleComparison(item)}
    />
  );
}
