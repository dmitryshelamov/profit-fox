import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import { botDetails, botId, emaDefinition, emaSummary, savedStrategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const createdId = '99999999-9999-4999-8999-999999999999';

function routes(extra: Record<string, (request: Request) => Response> = {}) {
  return {
    [`GET /api/bots/${botId}`]: () => json(botDetails),
    'GET /api/strategies': () => json([emaSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () =>
      json({ ...savedStrategy(emaSummary, emaDefinition), version: 7 }),
    'GET /api/market-data/instruments': () =>
      json([{ category: 'linear', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' }]),
    ...extra,
  };
}

const created = () => json({ id: createdId }, 201);

async function setField(name: string, text: string) {
  const field = await screen.findByLabelText(name);
  await userEvent.clear(field);
  if (text !== '') {
    await userEvent.type(field, text);
  }
}

async function chooseRange(parameter: string) {
  const row = (await screen.findByText(parameter, { selector: 'th *, th' })).closest('tr')!;
  await userEvent.click(within(row).getByRole('button', { name: 'Диапазон' }));
}

async function setRange(parameter: string, min: string, max: string, step: string) {
  await chooseRange(parameter);
  await setField(`${parameter} min`, min);
  await setField(`${parameter} max`, max);
  await setField(`${parameter} шаг`, step);
}

const submitted = (requests: Request[]) =>
  requests.find((request) => request.method === 'POST')!.json();

describe('new backtest form', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  // Scenario "Период по умолчанию".
  it('offers the 30 days up to the last closed candle of the bot interval', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date('2026-11-02T12:30:00Z') });
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    expect(await screen.findByLabelText('Начало периода')).toHaveValue('2026-10-03 12:00');
    expect(screen.getByLabelText('Конец периода')).toHaveValue('2026-11-02 12:00');
    expect(await screen.findByText(/последняя версия стратегии v7/)).toBeInTheDocument();
  });

  it('moves the period back from the same boundary by the quick buttons', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date('2026-11-02T12:30:00Z') });
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: '90 дней' }));
    expect(screen.getByLabelText('Начало периода')).toHaveValue('2026-08-04 12:00');
    await userEvent.click(screen.getByRole('button', { name: '1 год' }));
    expect(screen.getByLabelText('Начало периода')).toHaveValue('2025-11-02 12:00');
    expect(screen.getByLabelText('Конец периода')).toHaveValue('2026-11-02 12:00');
    await userEvent.click(screen.getByRole('button', { name: '30 дней' }));
    expect(screen.getByLabelText('Начало периода')).toHaveValue('2026-10-03 12:00');
  });

  // Scenario "Диапазон параметра".
  it('shows how many values a range has', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await setRange('fast', '5', '15', '5');

    expect(screen.getByLabelText('fast значений')).toHaveTextContent('3');
    expect(screen.getByLabelText('slow значений')).toHaveTextContent('1');
  });

  // Scenario "Две сетки".
  it('shows the product of the counts of the parameters', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await setRange('fast', '5', '15', '5');
    await setRange('slow', '20', '50', '10');

    expect(screen.getByText('Комбинаций: 12')).toBeInTheDocument();
  });

  // Scenario "Шаг не делит диапазон".
  it('names the parameter whose step does not divide the range instead of a number', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await setRange('fast', '5', '15', '5');
    await setRange('slow', '20', '45', '10');

    expect(screen.getByText('Комбинаций: ? — исправьте диапазон slow')).toBeInTheDocument();
    expect(screen.getByText('slow: max − min не делится на шаг 10.')).toBeInTheDocument();
    expect(screen.getByLabelText('slow значений')).toHaveTextContent('—');
  });

  it('shows the value of the bot or the default of the strategy as a hint', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    expect(await screen.findByLabelText('fast значение')).toHaveAttribute('placeholder', '10 бот');
    expect(screen.getByLabelText('slow значение')).toHaveAttribute(
      'placeholder',
      '26 по умолчанию',
    );
  });

  // Scenario "Без настроек".
  it('creates the backtest without simulation settings and opens it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date('2026-11-02T12:30:00Z') });
    const requests = fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: created,
        [`GET /api/backtests/${createdId}`]: () => problem(404, 'later'),
      }),
    );
    const { history } = renderApp(`/bots/${botId}/backtests/new`);

    await setField('fast значение', '12');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/backtests/${createdId}`));
    expect(await submitted(requests)).toEqual({
      from: '2026-10-03T12:00:00Z',
      to: '2026-11-02T12:00:00Z',
      parameters: { fast: 12 },
    });
  });

  it('sends the simulation settings that were filled', async () => {
    const requests = fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: created,
        [`GET /api/backtests/${createdId}`]: () => problem(404, 'later'),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByText('Настройки симуляции'));
    await setField('Баланс', '2000');
    await setField('Комиссия лимитки, %', '0.02');
    await setField('Комиссия рынка, %', '0.055');
    await setField('Проскальзывание, %', '0.1');
    await userEvent.selectOptions(screen.getByLabelText('Исполнение лимиток'), 'touch');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    expect((await submitted(requests)).simulation).toEqual({
      balance: 2000,
      limitFeePercent: 0.02,
      marketFeePercent: 0.055,
      slippagePercent: 0.1,
      limitFillMode: 'touch',
    });
  });

  it('sends only the fill mode when that is the only simulation setting chosen', async () => {
    const requests = fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: created,
        [`GET /api/backtests/${createdId}`]: () => problem(404, 'later'),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByText('Настройки симуляции'));
    await userEvent.selectOptions(screen.getByLabelText('Исполнение лимиток'), 'breakout');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    expect((await submitted(requests)).simulation).toEqual({ limitFillMode: 'breakout' });
  });

  it('sends the fields that were filled without a fill mode', async () => {
    const requests = fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: created,
        [`GET /api/backtests/${createdId}`]: () => problem(404, 'later'),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByText('Настройки симуляции'));
    await setField('Баланс', '2000');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    expect((await submitted(requests)).simulation).toEqual({ balance: 2000 });
  });

  it('marks a simulation field that is no number and sends nothing', async () => {
    const requests = fakeApi(routes({ [`POST /api/bots/${botId}/backtests`]: created }));
    const { history } = renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByText('Настройки симуляции'));
    await setField('Баланс', '0,1');
    await setField('Проскальзывание, %', 'abc');
    await setField('Комиссия рынка, %', '0.055');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    expect(await screen.findAllByText('Введите число.')).toHaveLength(2);
    expect(screen.getByLabelText('Баланс')).toHaveClass('border-err');
    expect(screen.getByLabelText('Комиссия рынка, %')).not.toHaveClass('border-err');
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
    expect(history.location.pathname).toBe(`/bots/${botId}/backtests/new`);
  });

  it('opens the simulation settings where a field is no number', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByText('Настройки симуляции'));
    await setField('Баланс', 'abc');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    expect(await screen.findByText('Введите число.')).toBeVisible();
  });

  // Scenario "Сетка больше лимита".
  it('shows the combinations limit error at the counter and keeps the values', async () => {
    const message = 'The grid has 27000 combinations, more than the limit of 10000.';
    fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: () =>
          problem(400, 'x', { errors: { parameters: [message] } }),
      }),
    );
    const { history } = renderApp(`/bots/${botId}/backtests/new`);

    await setRange('fast', '5', '15', '5');
    await userEvent.click(screen.getByRole('button', { name: 'Запустить' }));

    const counter = await screen.findByText('Комбинаций: 3');
    expect(counter.closest('section')).toHaveTextContent(message);
    expect(screen.getByLabelText('fast min')).toHaveValue('5');
    expect(screen.getByLabelText('fast max')).toHaveValue('15');
    expect(history.location.pathname).toBe(`/bots/${botId}/backtests/new`);
  });

  it('marks the range fields while the range cannot be split', async () => {
    fakeApi(routes());
    renderApp(`/bots/${botId}/backtests/new`);

    await setRange('fast', '15', '5', '5');

    for (const field of ['fast min', 'fast max', 'fast шаг']) {
      expect(screen.getByLabelText(field)).toHaveClass('border-err');
    }
    await setField('fast min', '5');
    expect(screen.getByLabelText('fast min')).not.toHaveClass('border-err');
  });

  it('shows the error of a parameter at that parameter', async () => {
    fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: () =>
          problem(400, 'x', { errors: { 'parameters.fast': ['Не меньше 2'] } }),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByText('Не меньше 2')).toBeInTheDocument();
    expect(screen.getByLabelText('fast значение')).toHaveClass('border-err');
  });

  it('shows the error of the period at the period', async () => {
    fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: () =>
          problem(400, 'x', { errors: { to: ['Последняя свеча ещё не закрыта.'] } }),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByText('Последняя свеча ещё не закрыта.')).toBeInTheDocument();
  });

  it('opens the simulation settings when one of them is rejected', async () => {
    fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: () =>
          problem(400, 'x', { errors: { 'simulation.balance': ['Баланс больше нуля.'] } }),
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByText('Баланс больше нуля.')).toBeVisible();
  });

  it('shows the message of any other failure above the buttons', async () => {
    fakeApi(routes({ [`POST /api/bots/${botId}/backtests`]: () => problem(404, 'Бот не найден') }));
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Бот не найден');
  });

  it('says the server cannot be reached when the request does not get an answer', async () => {
    fakeApi(
      routes({
        [`POST /api/bots/${botId}/backtests`]: () => {
          throw new TypeError('Failed to fetch');
        },
      }),
    );
    renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось связаться с сервером.');
    expect(screen.getByRole('button', { name: 'Запустить' })).toBeEnabled();
  });

  it('says when the bot cannot be loaded', async () => {
    fakeApi({ [`GET /api/bots/${botId}`]: () => problem(404, 'нет') });
    renderApp(`/bots/${botId}/backtests/new`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бота.');
  });

  // Scenario "Биржа недоступна".
  it('says the exchange is unavailable above the buttons and creates nothing', async () => {
    fakeApi(
      routes({ [`POST /api/bots/${botId}/backtests`]: () => problem(503, 'Bybit не отвечает') }),
    );
    const { history } = renderApp(`/bots/${botId}/backtests/new`);

    await userEvent.click(await screen.findByRole('button', { name: 'Запустить' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Биржа недоступна');
    expect(history.location.pathname).toBe(`/bots/${botId}/backtests/new`);
  });
});
