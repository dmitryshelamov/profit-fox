import { moveBefore } from './order';

describe('order after a drag', () => {
  it('puts the dragged row at the place of the row it is dropped on, going up', () => {
    expect(moveBefore(['a', 'b', 'c'], 'c', 'a')).toEqual(['c', 'a', 'b']);
  });

  it('puts the dragged row at the place of the row it is dropped on, going down', () => {
    expect(moveBefore(['a', 'b', 'c'], 'a', 'c')).toEqual(['b', 'c', 'a']);
  });

  it('leaves the order when a row is dropped on itself', () => {
    expect(moveBefore(['a', 'b', 'c'], 'b', 'b')).toEqual(['a', 'b', 'c']);
  });
});
