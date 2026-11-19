import type { PinType } from '../model/types';

/** The color of a pin and of the wires from it, by type. */
export const pinColors: Record<PinType, string> = {
  number: '#38bdf8',
  boolean: '#a78bfa',
  duration: '#34d399',
  event: '#facc15',
};

/** The color of the header of a node, by its group in the palette. */
export function headerColor(group: string | undefined): string {
  return group === 'Данные' ? '#0e7490' : group === 'Граф' ? '#52525b' : '#6d28d9';
}
