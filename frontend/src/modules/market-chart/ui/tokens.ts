/** The value of a theme token, for the chart library that draws on a canvas and cannot read CSS variables itself. */
export function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}
