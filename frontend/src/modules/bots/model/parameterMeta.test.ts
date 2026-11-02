import { parameterMeta } from './parameterMeta';

describe('parameter meta', () => {
  it('names an integer with its bounds', () => {
    expect(parameterMeta({ name: 'fast', type: 'number', integer: true, min: 2, max: 200 })).toBe(
      'целое · 2…200',
    );
  });

  it('names a number and a duration', () => {
    expect(parameterMeta({ name: 'stop', type: 'number', min: 0.5, max: 10 })).toBe(
      'число · 0.5…10',
    );
    expect(parameterMeta({ name: 'cooldown', type: 'duration' })).toBe('длительность');
  });

  it('leaves the bounds out unless both are declared', () => {
    expect(parameterMeta({ name: 'stop', type: 'number', min: 1 })).toBe('число');
  });
});
