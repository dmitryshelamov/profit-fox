import { levelsOf } from './levels';

describe('levelsOf', () => {
  it('names the entry, the stop and the target of an idea', () => {
    expect(levelsOf({ entry: '2.40', stop: '2.30', target: '2.60' })).toEqual([
      { kind: 'entry', price: '2.40', title: 'Вход 2.40' },
      { kind: 'stop', price: '2.30', title: 'Стоп 2.30' },
      { kind: 'target', price: '2.60', title: 'Цель 2.60' },
    ]);
  });
});
