import { ApiError } from '@/shared/api/client';

import { accountFieldErrors, accountProblem } from './accountProblems';

describe('account problems', () => {
  it('lays out the errors of the account fields', () => {
    expect(
      accountFieldErrors({
        name: ['Name must not be empty.'],
        apiKey: ['Bybit does not accept the key.'],
        other: ['Something else.'],
      }),
    ).toEqual({
      fields: { name: ['Name must not be empty.'], apiKey: ['Bybit does not accept the key.'] },
      form: ['Something else.'],
    });
  });

  // Scenario "Аккаунт с ботами".
  it('names the bots that keep an account from being deleted', () => {
    const error = new ApiError(
      409,
      'Bots trade with the exchange account.',
      {},
      [],
      [],
      ['BTC EMA час'],
    );

    expect(accountProblem(error)).toBe('Аккаунт используют боты: BTC EMA час.');
  });

  it('says that bybit is unavailable', () => {
    expect(accountProblem(new ApiError(503, 'Bybit is unavailable.'))).toBe(
      'Bybit недоступен, попробуйте позже.',
    );
  });

  // Bybit answered and refused: waiting will not help, so the reason is shown instead of "try later".
  it('shows the reason bybit refused the request', () => {
    expect(
      accountProblem(new ApiError(502, '10002: please check your server timestamp or recv_window')),
    ).toBe('Bybit отклонил запрос: 10002: please check your server timestamp or recv_window');
  });

  it('keeps the message of any other answer', () => {
    expect(accountProblem(new ApiError(409, 'Bybit does not accept the key.'))).toBe(
      'Bybit does not accept the key.',
    );
  });

  it('says the server is out of reach when there is no answer', () => {
    expect(accountProblem(new TypeError('Failed to fetch'))).toBe(
      'Не удалось связаться с сервером.',
    );
  });
});
