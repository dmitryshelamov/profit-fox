/** The columns of the list of ideas that sort it; the API takes the same codes. */
export const ideaSortColumns = [
  'firedAt',
  'symbol',
  'setup',
  'interval',
  'type',
  'side',
  'entry',
  'stop',
  'target',
  'riskReward',
  'probability',
  'expectancy',
  'profit',
  'status',
] as const;

export type IdeaSortColumn = (typeof ideaSortColumns)[number];

export type SortDirection = 'asc' | 'desc';

export interface IdeaSort {
  readonly column: IdeaSortColumn;
  readonly direction: SortDirection;
}
