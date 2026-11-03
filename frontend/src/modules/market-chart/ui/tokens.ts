/** The value of a theme token, for the chart library that draws on a canvas and cannot read CSS variables itself. */
export function token(name: string, fallback: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
}

/** A `#rrggbb` colour with transparency, for the candle that is not closed and the volume columns. */
export function withAlpha(colour: string, alpha: number): string {
  const [red = 0, green = 0, blue = 0] = [1, 3, 5].map((start) =>
    parseInt(colour.slice(start, start + 2), 16),
  );

  return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
}
