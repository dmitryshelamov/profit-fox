import {
  autoFieldErrors,
  bodyOfForm,
  changedFields,
  changedText,
  formOfAuto,
  formOfDefaults,
} from './autoForm';

const response = {
  id: 'auto-1',
  name: 'Импульс лонг',
  accountId: 'account-1',
  accountName: 'Bybit · ideas',
  amount: '300',
  mode: 'live',
  setups: ['impulse'],
  types: ['pullback'],
  intervals: ['15'],
  side: 'long',
  category: null,
  badge: 'up',
  minRiskReward: '1.5',
  minCases: 100,
  position: 0,
};

describe('form of an auto', () => {
  it('takes the account, the amount and the lowest R:R of a new auto from the settings, and leaves the conditions empty', () => {
    expect(formOfDefaults({ accountId: 'account-1', amount: '300', minRiskReward: '1.5' })).toEqual(
      {
        name: '',
        accountId: 'account-1',
        amount: '300',
        mode: 'off',
        setups: [],
        types: [],
        intervals: [],
        side: '',
        category: '',
        badge: '',
        minRiskReward: '1.5',
        minCases: '0',
      },
    );
  });

  it('has no account in a new auto while the settings have none', () => {
    expect(formOfDefaults({ accountId: null, amount: '20', minRiskReward: '1.5' }).accountId).toBe(
      '',
    );
  });

  it('turns the auto of the API into the form: no side, market or badge is an empty choice', () => {
    expect(formOfAuto(response)).toEqual({
      name: 'Импульс лонг',
      accountId: 'account-1',
      amount: '300',
      mode: 'live',
      setups: ['impulse'],
      types: ['pullback'],
      intervals: ['15'],
      side: 'long',
      category: '',
      badge: 'up',
      minRiskReward: '1.5',
      minCases: '100',
    });
  });

  it('turns the form into the body: an empty choice is none, the numbers keep their digits, the mode stays out', async () => {
    const body = bodyOfForm({ ...formOfAuto(response), amount: '300.50', badge: '' });

    expect(JSON.parse(JSON.stringify(body))).toMatchObject({
      name: 'Импульс лонг',
      accountId: 'account-1',
      setups: ['impulse'],
      types: ['pullback'],
      intervals: ['15'],
      side: 'long',
      category: null,
      badge: null,
    });
    expect(body).not.toHaveProperty('mode');
    const { serializeJson } = await import('@/shared/api/jsonNumber');
    expect(serializeJson(body)).toContain('"amount":300.50');
    expect(serializeJson(body)).toContain('"minCases":100');
  });

  it('sends a number that is not one as it is, so the API names the field', () => {
    const body = bodyOfForm({ ...formOfAuto(response), amount: 'abc' });

    expect(body.amount).toBe('abc');
  });

  it('lists the fields that changed in the order of the form, and none when nothing did', () => {
    const initial = formOfAuto(response);

    expect(changedFields(initial, initial)).toEqual([]);
    expect(
      changedFields(initial, { ...initial, minRiskReward: '1.8', intervals: ['15', '60'] }),
    ).toEqual(['ТФ', 'минимальный R:R']);
    expect(changedFields(initial, { ...initial, name: 'Другой', mode: 'paper' })).toEqual([
      'название',
      'режим',
    ]);
  });

  it('does not count the same chips ticked in another order as a change', () => {
    const initial = { ...formOfAuto(response), setups: ['impulse', 'drop'] };

    expect(changedFields(initial, { ...initial, setups: ['drop', 'impulse'] })).toEqual([]);
  });

  it('writes the changes as a line next to the button', () => {
    expect(changedText(['ТФ', 'минимальный R:R'])).toBe('изменено: ТФ, минимальный R:R');
    expect(changedText([])).toBe('');
  });

  it('lays the errors of the API out by the field of the form', () => {
    const laid = autoFieldErrors({ amount: ['Too much.'], other: ['Oops.'] });

    expect(laid.fields.amount).toEqual(['Too much.']);
    expect(laid.form).toEqual(['Oops.']);
  });
});
