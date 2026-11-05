/** The sizes of a page the API takes; the first is the default. */
export const pageSizes = [50, 100, 200] as const;

export const defaultPageSize = pageSizes[0];

/** A size kept in the browser when it is one the API takes, otherwise the default: a stored value may be stale. */
export function pageSizeOf(value: unknown): number {
  return pageSizes.find((size) => size === value) ?? defaultPageSize;
}
