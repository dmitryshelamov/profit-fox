import { formOfSettings, settingsFieldErrors } from './settingsForm';

describe('settings form', () => {
  it('holds every number of the settings as text', () => {
    expect(
      formOfSettings({
        accountId: null,
        risk: '1',
        maxAmount: '200',
        maxOpenTrades: 5,
        maxTotalRisk: '5',
        minRiskReward: '1.5',
      }),
    ).toEqual({
      accountId: '',
      risk: '1',
      maxAmount: '200',
      maxOpenTrades: '5',
      maxTotalRisk: '5',
      minRiskReward: '1.5',
    });
  });

  it('lays server errors out by field and keeps the rest for the form', () => {
    const laid = settingsFieldErrors({ risk: ['The risk must be above zero.'], other: ['x'] });

    expect(laid.fields).toEqual({ risk: ['The risk must be above zero.'] });
    expect(laid.form).toEqual(['x']);
  });
});
