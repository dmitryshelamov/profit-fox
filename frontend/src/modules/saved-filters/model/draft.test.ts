import { type Draft, draftOf, switchField } from './draft';

const number = { code: 'change1h', operators: ['>', '<', 'between'], choices: null };
const side = { code: 'side', operators: ['='], choices: ['long', 'short'] };
const exact = { code: 'turnover24h', operators: ['>', '='], choices: null };
const probability = {
  code: 'probability',
  operators: ['>', 'above_baseline'],
  choices: null,
};

const draft = (overrides: Partial<Draft> = {}): Draft => ({
  field: 'change1h',
  operator: '>',
  value: '3',
  to: '',
  ...overrides,
});

describe('draftOf', () => {
  it('starts a numeric field with its first operator and no value', () => {
    expect(draftOf(number)).toEqual({ field: 'change1h', operator: '>', value: '', to: '' });
  });

  it('starts a choice field with its first choice', () => {
    expect(draftOf(side)).toEqual({ field: 'side', operator: '=', value: 'long', to: '' });
  });
});

describe('switchField', () => {
  it('keeps the operator and the number when the new field takes them', () => {
    expect(switchField(draft({ operator: '<', value: '5' }), number, number)).toEqual(
      draft({ operator: '<', value: '5' }),
    );
    expect(switchField(draft({ operator: '<', value: '5' }), number, probability).operator).toBe(
      '>',
    );
    expect(switchField(draft({ operator: '>', value: '5' }), number, probability).value).toBe('5');
  });

  it('does not carry the «=» of a choice over to a numeric field that takes it', () => {
    expect(
      switchField(draft({ field: 'side', operator: '=', value: 'long' }), side, exact).operator,
    ).toBe('>');
    expect(switchField(draft({ operator: '=', value: '3' }), number, exact).operator).toBe('=');
  });

  it('keeps the upper bound for a numeric field and drops it for a choice', () => {
    const range = draft({ operator: 'between', value: '1', to: '7' });

    expect(switchField(range, number, number).to).toBe('7');
    expect(switchField(range, number, side).to).toBe('');
  });

  it('falls back to the first operator when the new field does not take the old one', () => {
    expect(switchField(draft({ operator: 'between', to: '7' }), number, probability).operator).toBe(
      '>',
    );
  });

  it('takes the first choice when the field turns into a choice, and drops it going back to numbers', () => {
    const chosen = switchField(draft(), number, side);

    expect(chosen).toEqual({ field: 'side', operator: '=', value: 'long', to: '' });
    expect(switchField(chosen, side, number)).toEqual({
      field: 'change1h',
      operator: '>',
      value: '',
      to: '',
    });
  });

  it('keeps the choice when the new field has it', () => {
    const type = { code: 'type', operators: ['='], choices: ['long', 'pullback'] };

    expect(
      switchField(draft({ field: 'side', operator: '=', value: 'long' }), side, type).value,
    ).toBe('long');
  });
});
