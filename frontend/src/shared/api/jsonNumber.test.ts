import { isDecimalText, jsonNumberOf, serializeJson } from './jsonNumber';

describe('isDecimalText', () => {
  it.each(['0', '10', '-5', '+5', '0.055', '1000.12345678901234567', '007'])(
    'accepts %s',
    (text) => {
      expect(isDecimalText(text)).toBe(true);
    },
  );

  it.each(['', ' ', '0x10', '1e5', 'abc', '0,1', '.5', '5.', '--1', '1 000', 'Infinity'])(
    'rejects "%s"',
    (text) => {
      expect(isDecimalText(text)).toBe(false);
    },
  );
});

describe('serializeJson', () => {
  it('writes a number with exactly the digits of its text', () => {
    const body = {
      fast: jsonNumberOf('12.123456789012345678901'),
      big: jsonNumberOf('9007199254740993'),
    };

    expect(serializeJson(body)).toBe('{"fast":12.123456789012345678901,"big":9007199254740993}');
  });

  it('writes the number the way JSON spells it: no plus, no leading zeros', () => {
    expect(
      serializeJson([jsonNumberOf('+007.50'), jsonNumberOf('-0.5'), jsonNumberOf('000')]),
    ).toBe('[7.50,-0.5,0]');
  });

  it('writes anything else as JSON.stringify does, even text that looks like a number', () => {
    const body = {
      name: 'a"b',
      list: [1, null, true],
      text: '12.5',
      nested: { fast: jsonNumberOf('3') },
    };

    expect(serializeJson(body)).toBe(JSON.stringify({ ...body, nested: { fast: 3 } }));
  });
});
