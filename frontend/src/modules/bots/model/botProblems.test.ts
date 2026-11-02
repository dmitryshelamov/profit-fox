import { botFieldErrors } from './botProblems';

describe('bot validation problems', () => {
  it.each(['name', 'strategyId', 'category', 'symbol', 'balance', 'interval'])(
    'puts %s on its field',
    (key) => {
      expect(botFieldErrors({ [key]: ['bad'] })).toEqual({ fields: { [key]: ['bad'] }, form: [] });
    },
  );

  it('puts a parameter value on the field of that parameter', () => {
    expect(botFieldErrors({ 'parameterValues.fast': ['Out of range.'] })).toEqual({
      fields: { 'parameterValues.fast': ['Out of range.'] },
      form: [],
    });
  });

  it('puts the whole parameter object, which has no field, into the form', () => {
    expect(
      botFieldErrors({ parameterValues: ['Parameter values must be a JSON object.'] }),
    ).toEqual({
      fields: {},
      form: ['Parameter values must be a JSON object.'],
    });
  });

  it('puts a key that matches no field into the form', () => {
    expect(botFieldErrors({ $: ['The JSON value could not be converted.'] })).toEqual({
      fields: {},
      form: ['The JSON value could not be converted.'],
    });
  });

  it('splits a mixed answer', () => {
    expect(botFieldErrors({ symbol: ['No such symbol.'], other: ['a', 'b'] })).toEqual({
      fields: { symbol: ['No such symbol.'] },
      form: ['a', 'b'],
    });
  });

  it('has no errors when the answer has none', () => {
    expect(botFieldErrors(undefined)).toEqual({ fields: {}, form: [] });
  });
});
