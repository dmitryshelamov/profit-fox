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
  budget: '120.5',
  dayDrop: 'with',
  btcDrop: 'any',
  highFunding: 'without',
};

describe('form of an auto', () => {
  it('takes the account, the amount and the lowest R:R of a new auto from the settings, and leaves the conditions empty', () => {
    expect(formOfDefaults({ accountId: 'account-1', amount: '300', minRiskReward: '1.5' })).toEqual(
      {
        name: '',
        accountId: 'account-1',
        amount: '300',
        budget: '',
        mode: 'off',
        setups: [],
        types: [],
        intervals: [],
        side: '',
        category: '',
        badge: '',
        minRiskReward: '1.5',
        minCases: '0',
        dayDrop: 'any',
        btcDrop: 'any',
        highFunding: 'any',
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
      budget: '120.5',
      mode: 'live',
      setups: ['impulse'],
      types: ['pullback'],
      intervals: ['15'],
      side: 'long',
      category: '',
      badge: 'up',
      minRiskReward: '1.5',
      minCases: '100',
      dayDrop: 'with',
      btcDrop: 'any',
      highFunding: 'without',
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
      dayDrop: 'with',
      btcDrop: 'any',
      highFunding: 'without',
    });
    expect(body).not.toHaveProperty('mode');
    const { serializeJson } = await import('@/shared/api/jsonNumber');
    expect(serializeJson(body)).toContain('"amount":300.50');
    expect(serializeJson(body)).toContain('"minCases":100');
  });

  it('has no budget in a form of an auto without one', () => {
    expect(formOfAuto({ ...response, budget: null }).budget).toBe('');
  });

  it('sends an empty budget as none and a budget with its digits', async () => {
    const { serializeJson } = await import('@/shared/api/jsonNumber');

    expect(bodyOfForm({ ...formOfAuto(response), budget: '' }).budget).toBeNull();
    expect(serializeJson(bodyOfForm({ ...formOfAuto(response), budget: '150.50' }))).toContain(
      '"budget":150.50',
    );
    expect(bodyOfForm({ ...formOfAuto(response), budget: 'abc' }).budget).toBe('abc');
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
    expect(changedFields(initial, { ...initial, budget: '', amount: '200' })).toEqual([
      'сумма',
      'бюджет',
    ]);
    expect(changedFields(initial, { ...initial, highFunding: 'any' })).toEqual(['высокий funding']);
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
    const laid = autoFieldErrors({
      amount: ['Too much.'],
      budget: ['Too little.'],
      other: ['Oops.'],
    });

    expect(laid.fields.amount).toEqual(['Too much.']);
    expect(laid.fields.budget).toEqual(['Too little.']);
    expect(laid.form).toEqual(['Oops.']);
  });
});
