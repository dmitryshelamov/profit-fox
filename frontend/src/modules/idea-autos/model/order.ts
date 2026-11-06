/** The ids after the row `dragged` is dropped on the row `target`: it takes the place of the target and the rows between shift by one. */
export function moveBefore(ids: readonly string[], dragged: string, target: string): string[] {
  const rest = ids.filter((id) => id !== dragged);
  const index = ids.indexOf(target);

  return [...rest.slice(0, index), dragged, ...rest.slice(index)];
}
