import { formOfSettings, settingsFieldErrors } from './settingsForm';

describe('settings form', () => {
  it('holds every number of the settings as text', () => {
    expect(
      formOfSettings({
        accountId: null,
        amount: '20',
        maxAmount: '200',
        maxOpenTrades: 5,
        maxTotalRisk: '5',
        minRiskReward: '1.5',
        dailyLossLimit: '30',
        maxSameDirection: 5,
        autosBudget: '200',
      }),
    ).toEqual({
      accountId: '',
      amount: '20',
      maxAmount: '200',
      maxOpenTrades: '5',
      maxTotalRisk: '5',
      minRiskReward: '1.5',
      dailyLossLimit: '30',
      maxSameDirection: '5',
      autosBudget: '200',
    });
  });

  it('holds a guard that is off as an empty field', () => {
    const form = formOfSettings({
      accountId: null,
      amount: '20',
      maxAmount: '200',
      maxOpenTrades: 5,
      maxTotalRisk: '5',
      minRiskReward: '1.5',
      dailyLossLimit: null,
      maxSameDirection: null,
      autosBudget: null,
    });

    expect(form.dailyLossLimit).toBe('');
    expect(form.autosBudget).toBe('');
    expect(form.maxSameDirection).toBe('');
  });

  it('lays server errors out by field and keeps the rest for the form', () => {
    const laid = settingsFieldErrors({ amount: ['The amount must be above zero.'], other: ['x'] });

    expect(laid.fields).toEqual({ amount: ['The amount must be above zero.'] });
    expect(laid.form).toEqual(['x']);
  });
});
