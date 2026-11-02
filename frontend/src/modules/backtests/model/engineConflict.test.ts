import { engineConflict, whyDetailsUnavailable } from './engineConflict';

describe('engine conflict', () => {
  it('reads both versions from the answer of the API', () => {
    expect(
      engineConflict(
        'The backtest was computed at engine version 2, and the details would be computed at engine version 3.',
      ),
    ).toEqual({ computed: '2', current: '3' });
  });

  it('has no versions in an answer of another kind', () => {
    expect(engineConflict('Something else.')).toBeUndefined();
  });
});

describe('why the details are unavailable', () => {
  it('names both engine versions', () => {
    expect(
      whyDetailsUnavailable(
        'The backtest was computed at engine version 2, and the details would be computed at engine version 3.',
      ),
    ).toBe('бэктест посчитан движком v2, а API работает с движком v3.');
  });

  it('gives the answer of the API when it names no versions', () => {
    expect(whyDetailsUnavailable('Сбой расчёта.')).toBe('Сбой расчёта.');
  });
});
