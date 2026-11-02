import type { FillMarker } from './pricePoint';

export interface SeriesMarker {
  readonly time: number;
  readonly position: 'belowBar' | 'aboveBar';
  readonly shape: 'arrowUp' | 'arrowDown';
  readonly kind: 'entry' | 'exit';
  /** The number of fills when there are several, empty for one. */
  readonly text: string;
}

/** A buy points up from below the bar, a sell points down from above it; the kind tells entry from exit. */
export function seriesMarkers(markers: readonly FillMarker[]): SeriesMarker[] {
  return markers.map((marker) => ({
    time: marker.time,
    position: marker.side === 'buy' ? 'belowBar' : 'aboveBar',
    shape: marker.side === 'buy' ? 'arrowUp' : 'arrowDown',
    kind: marker.kind,
    text: marker.count > 1 ? String(marker.count) : '',
  }));
}
