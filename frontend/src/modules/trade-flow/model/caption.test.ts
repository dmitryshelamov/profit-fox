import { barCaption } from './caption';

const bar = (buyTurnover: string, sellTurnover: string) => ({ buyTurnover, sellTurnover });

describe('barCaption', () => {
  it('writes the buys, the sells and their difference of a bar in compact USDT', () => {
    expect(barCaption(bar('451', '447'))).toEqual({ buys: '451', sells: '447', difference: '+4' });
    expect(barCaption(bar('1200000', '3450000.5'))).toEqual({
      buys: '1.2 млн',
      sells: '3.5 млн',
      difference: '−2.3 млн',
    });
    expect(barCaption(bar('10', '10'))).toEqual({ buys: '10', sells: '10', difference: '0' });
  });
});
