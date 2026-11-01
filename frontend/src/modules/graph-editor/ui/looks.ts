/** How nodes look on the canvas and in the palette: the groups and colors of the mockups. */
export interface NodeGroup {
  readonly title: string;
  readonly color: string;
  readonly kinds: readonly string[];
}

export const nodeGroups: readonly NodeGroup[] = [
  {
    title: 'Данные',
    color: '#2e5f96',
    kinds: ['parameters', 'inputs', 'outputs', 'candle', 'position'],
  },
  { title: 'Индикаторы', color: '#1f7a75', kinds: ['ema', 'cross'] },
  {
    title: 'Математика',
    color: '#4a5263',
    kinds: ['add', 'subtract', 'multiply', 'divide', 'compare'],
  },
  { title: 'Логика', color: '#6b4a8f', kinds: ['and', 'or', 'not'] },
  { title: 'Ордера', color: '#a3482c', kinds: ['market-order', 'close-position', 'hold-limit'] },
];

export const compositeColor = '#8a6420';

export function colorOf(kind: string): string {
  return nodeGroups.find((group) => group.kinds.includes(kind))?.color ?? compositeColor;
}

/** Russian names of setting values, as the node card offers them and the node shows them. */
export const settingTitles: Record<string, Record<string, string>> = {
  side: { buy: 'Покупка', sell: 'Продажа' },
  size: { quantity: 'Объём', amount: 'Сумма' },
  operation: { gt: 'больше', ge: 'не меньше', lt: 'меньше', le: 'не больше' },
  type: { number: 'число', duration: 'длительность' },
};

export const settingNames: Record<string, string> = {
  side: 'Сторона',
  size: 'Размер',
  operation: 'Операция',
  type: 'Тип значений',
};

export function settingsSummary(settings: Readonly<Record<string, string>> | undefined): string {
  return Object.entries(settings ?? {})
    .map(([name, value]) => settingTitles[name]?.[value] ?? value)
    .join(' · ');
}
