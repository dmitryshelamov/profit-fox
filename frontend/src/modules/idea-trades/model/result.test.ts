import { resultText } from './result';

describe('result of a trade', () => {
  it('shows the result in money and in R with its sign', () => {
    expect(resultText('4.68', '23.4')).toBe('+4.68 USDT · +23.40 R');
    expect(resultText('-0.31', '-1.0512')).toBe('-0.31 USDT · -1.05 R');
  });

  it('shows nothing before the trade is closed', () => {
    expect(resultText(null, null)).toBe('—');
  });
});
