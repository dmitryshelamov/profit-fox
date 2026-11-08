import { brokerAccountProblem, brokerAccountStatus, brokerAccountType } from './brokerAccounts';

describe('broker accounts', () => {
  it.each([
    ['brokerage', 'Брокерский'],
    ['iis', 'ИИС'],
    ['other', 'Другой'],
    ['unknown', 'unknown'],
  ])('names the type %s as %s', (code, label) => {
    expect(brokerAccountType(code)).toBe(label);
  });

  it.each([
    ['open', 'открыт'],
    ['new', 'новый'],
    ['closed', 'закрыт'],
  ])('names the status %s as %s', (code, label) => {
    expect(brokerAccountStatus(code)).toBe(label);
  });

  // Scenario "Недоступный счёт": the row of an account that cannot be added shows why.
  it.each([
    ['not_open', 'не открыт'],
    ['not_full_access', 'нет полного доступа'],
    ['unsupported_type', 'не брокерский счёт и не ИИС'],
  ])('explains the problem %s as %s', (code, label) => {
    expect(brokerAccountProblem(code)).toBe(label);
  });

  it('has nothing to explain for an available account', () => {
    expect(brokerAccountProblem(null)).toBeNull();
  });
});
