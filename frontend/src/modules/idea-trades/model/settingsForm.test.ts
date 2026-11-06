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
      }),
    ).toEqual({
      accountId: '',
      amount: '20',
      maxAmount: '200',
      maxOpenTrades: '5',
      maxTotalRisk: '5',
      minRiskReward: '1.5',
    });
  });

  it('lays server errors out by field and keeps the rest for the form', () => {
    const laid = settingsFieldErrors({ amount: ['The amount must be above zero.'], other: ['x'] });

    expect(laid.fields).toEqual({ amount: ['The amount must be above zero.'] });
    expect(laid.form).toEqual(['x']);
  });
});
