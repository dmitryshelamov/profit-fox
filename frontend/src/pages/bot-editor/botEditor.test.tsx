import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { fakeApi, json, problem } from '@/test/fakeApi';
import {
  botDetails,
  botId,
  emaDefinition,
  emaShortSummary,
  emaSummary,
  exchangeAccount,
  savedStrategy,
} from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

const instruments = [
  { category: 'spot', symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' },
  { category: 'spot', symbol: 'BTCEUR', baseCoin: 'BTC', quoteCoin: 'EUR' },
  { category: 'spot', symbol: 'ETHBTC', baseCoin: 'ETH', quoteCoin: 'BTC' },
];

function routes(extra: Record<string, () => Response> = {}) {
  return {
    'GET /api/strategies': () => json([emaSummary, emaShortSummary]),
    [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, emaDefinition)),
    'GET /api/market-data/instruments': (request: Request) => {
      const url = new URL(request.url);
      const text = (url.searchParams.get('query') ?? '').toLowerCase();

      return json(
        instruments.filter(
          (item) =>
            item.category === url.searchParams.get('Category') &&
            item.symbol.toLowerCase().includes(text),
        ),
      );
    },
    ...extra,
  };
}

describe('bot form', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Бот создан".
  it('creates a bot and opens its page', async () => {
    const requests = fakeApi(
      routes({
        'POST /api/bots': () => json({ id: botId }, 201),
        [`GET /api/bots/${botId}`]: () =>
          json({ ...botDetails, category: 'spot', parameterValues: {} }),
        [`GET /api/bots/${botId}/backtests`]: () => json([]),
        'GET /api/bots': () => json([]),
      }),
    );
    const { history } = renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Название'), 'BTC EMA час');
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), 'EMA');
    await userEvent.click(screen.getByRole('button', { name: 'linear' }));
    await userEvent.type(screen.getByLabelText('Символ'), 'BTCUSDT');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '1000');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/bots/${botId}`));
    const post = requests.find((request) => request.method === 'POST')!;
    expect(await post.json()).toEqual({
      name: 'BTC EMA час',
      strategyId: emaSummary.id,
      parameterValues: {},
      category: 'linear',
      symbol: 'BTCUSDT',
      interval: '60',
      balance: '1000',
      exchangeAccountId: null,
    });
  });

  // Scenario "Выбор аккаунта".
  it('sends the chosen account', async () => {
    const requests = fakeApi(
      routes({
        'GET /api/exchange-accounts': () => json([exchangeAccount]),
        'POST /api/bots': () => json({ id: botId }, 201),
        [`GET /api/bots/${botId}`]: () =>
          json({
            ...botDetails,
            exchangeAccount: { id: exchangeAccount.id, name: 'Проверка' },
          }),
        [`GET /api/bots/${botId}/backtests`]: () => json([]),
        'GET /api/bots': () => json([]),
      }),
    );
    const { history } = renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await userEvent.type(await screen.findByLabelText('Название'), 'BTC EMA час');
    await userEvent.type(screen.getByLabelText('Символ'), 'BTCUSDT');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '1000');
    await userEvent.selectOptions(
      await screen.findByLabelText('Аккаунт'),
      await screen.findByRole('option', { name: 'Проверка' }),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/bots/${botId}`));
    const post = requests.find((request) => request.method === 'POST')!;
    expect((await post.json()).exchangeAccountId).toBe(exchangeAccount.id);
    expect(await screen.findByRole('link', { name: 'Проверка' })).toBeInTheDocument();
  });

  // Scenario "Ключ не торгует в категории".
  it('shows the server error of the account at its field and keeps the values', async () => {
    fakeApi(
      routes({
        'GET /api/exchange-accounts': () => json([exchangeAccount]),
        'POST /api/bots': () =>
          problem(400, 'x', {
            errors: { exchangeAccountId: ['The key of the account does not trade linear.'] },
          }),
      }),
    );
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await userEvent.type(await screen.findByLabelText('Название'), 'BTC EMA час');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '1000');
    await userEvent.selectOptions(
      await screen.findByLabelText('Аккаунт'),
      await screen.findByRole('option', { name: 'Проверка' }),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    expect(
      await screen.findByText('The key of the account does not trade linear.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Название')).toHaveValue('BTC EMA час');
    expect(screen.getByLabelText('Аккаунт')).toHaveValue(exchangeAccount.id);
  });

  it('offers to add an account when there is none', async () => {
    fakeApi(routes({ 'GET /api/exchange-accounts': () => json([]) }));
    renderApp('/bots/new');

    expect(await screen.findByRole('link', { name: 'Добавить аккаунт' })).toHaveAttribute(
      'href',
      '/accounts/new',
    );
  });

  // Money goes as text: a JavaScript number would round the digits.
  it('sends the balance and a parameter with every digit typed', async () => {
    const requests = fakeApi(routes({ 'POST /api/bots': () => problem(400, 'x') }));
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await userEvent.type(await screen.findByLabelText('Название'), 'Бот');
    await userEvent.type(screen.getByLabelText('fast'), '10.123456789012345678');
    await userEvent.type(screen.getByLabelText('Баланс бота'), ' 1000.12345678901234567');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    const text = await requests.find((request) => request.method === 'POST')!.text();
    expect(text).toContain('"balance":"1000.12345678901234567"');
    expect(text).toContain('"parameterValues":{"fast":10.123456789012345678}');
  });

  it.each(['', '0x10', '1e5', '1,5'])('rejects "%s" as a balance at the field', async (text) => {
    const requests = fakeApi(routes({ 'POST /api/bots': () => json({ id: botId }, 201) }));
    renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Название'), 'Бот');
    if (text !== '') {
      await userEvent.type(screen.getByLabelText('Баланс бота'), text);
    }
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    expect(await screen.findByText('Введите число.')).toBeInTheDocument();
    expect(screen.getByLabelText('Баланс бота')).toHaveClass('border-err');
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });

  // Scenario "Значение по умолчанию".
  it('shows the default of a parameter as a hint and leaves an empty field out', async () => {
    const requests = fakeApi(
      routes({ 'POST /api/bots': () => problem(400, 'x', { errors: { symbol: ['Нет'] } }) }),
    );
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    expect(await screen.findByLabelText('slow')).toHaveAttribute('placeholder', '26');
    await userEvent.type(screen.getByLabelText('fast'), '10');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '5');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    await screen.findByText('Нет');
    const post = requests.find((request) => request.method === 'POST')!;
    expect(((await post.json()) as { parameterValues: unknown }).parameterValues).toEqual({
      fast: 10,
    });
  });

  it('leaves the hint empty for a parameter without a default', async () => {
    fakeApi(
      routes({
        [`GET /api/strategies/${emaSummary.id}`]: () =>
          json(
            savedStrategy(emaSummary, {
              ...emaDefinition,
              parameters: [{ name: 'stop', type: 'number' }],
            }),
          ),
      }),
    );
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    expect(await screen.findByLabelText('stop')).toHaveAttribute('placeholder', '');
  });

  // Scenario "Создание из списка стратегий": the form starts with the chosen strategy.
  it('starts with the strategy from the link', async () => {
    fakeApi(routes());
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await waitFor(() => expect(screen.getByLabelText('Стратегия')).toHaveValue(emaSummary.id));
    expect(await screen.findByLabelText('fast')).toBeInTheDocument();
  });

  it('replaces the parameter fields when the strategy changes', async () => {
    fakeApi(routes());
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await screen.findByLabelText('fast');
    fakeApi(
      routes({
        [`GET /api/strategies/${emaShortSummary.id}`]: () =>
          json(
            savedStrategy(emaShortSummary, {
              ...emaDefinition,
              parameters: [{ name: 'stop', type: 'number' }],
            }),
          ),
      }),
    );
    await userEvent.selectOptions(screen.getByLabelText('Стратегия'), 'EMA Short');

    expect(await screen.findByLabelText('stop')).toBeInTheDocument();
    expect(screen.queryByLabelText('fast')).not.toBeInTheDocument();
  });

  it('asks for suggestions only once something is typed in the symbol', async () => {
    const requests = fakeApi(routes());
    renderApp('/bots/new');

    await screen.findByLabelText('Символ');
    await new Promise((resolve) => setTimeout(resolve, 400));
    const asked = () =>
      requests.filter((request) => request.url.includes('/api/market-data/instruments'));
    expect(asked()).toHaveLength(0);

    await userEvent.type(screen.getByLabelText('Символ'), ' ');
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(asked()).toHaveLength(0);

    await userEvent.type(screen.getByLabelText('Символ'), 'BTC');
    await waitFor(() => expect(asked()).toHaveLength(1));
    expect(new URL(asked()[0]!.url).searchParams.get('query')).toBe('BTC');
  });

  // Scenario "Подсказки по части символа".
  it('suggests instruments of the category that contain the typed text', async () => {
    fakeApi(routes());
    renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Символ'), 'BTC');

    await waitFor(() =>
      expect(
        [...document.querySelectorAll('#bot-symbols option')].map((option) => [
          option.getAttribute('value'),
          option.textContent,
        ]),
      ).toEqual([
        ['BTCUSDT', 'BTC / USDT'],
        ['BTCEUR', 'BTC / EUR'],
        ['ETHBTC', 'ETH / BTC'],
      ]),
    );
  });

  it('works without suggestions when the exchange does not answer', async () => {
    fakeApi(routes({ 'GET /api/market-data/instruments': () => problem(503, 'down') }));
    renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Символ'), 'BTC');

    expect(screen.getByLabelText('Символ')).toHaveValue('BTC');
    expect(document.querySelectorAll('#bot-symbols option')).toHaveLength(0);
  });

  // Scenario "Неизвестный символ".
  it('shows the server error at the symbol field and keeps the input', async () => {
    fakeApi(
      routes({
        'POST /api/bots': () =>
          problem(400, 'x', { errors: { symbol: ['Символ NOPEUSDT не найден.'] } }),
      }),
    );
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await userEvent.type(await screen.findByLabelText('Название'), 'Бот');
    await userEvent.type(screen.getByLabelText('Символ'), 'NOPEUSDT');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '5');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    expect(await screen.findByText('Символ NOPEUSDT не найден.')).toBeInTheDocument();
    expect(screen.getByLabelText('Символ')).toHaveValue('NOPEUSDT');
    expect(screen.getByRole('alert')).toHaveTextContent('Бот не сохранён');
  });

  // Scenario "Валюта котировки".
  it('labels the balance with the quote coin of the instrument', async () => {
    fakeApi(routes());
    renderApp('/bots/new');

    expect(screen.queryByText('BTC')).not.toBeInTheDocument();
    await userEvent.type(await screen.findByLabelText('Символ'), 'ETHBTC');

    expect(await screen.findByText('BTC')).toBeInTheDocument();
  });

  // Scenario "Значение вне ограничений".
  it('shows an error at the parameter and keeps the other values', async () => {
    fakeApi(
      routes({
        'POST /api/bots': () =>
          problem(400, 'x', { errors: { 'parameterValues.fast': ['Не меньше 2'] } }),
      }),
    );
    renderApp(`/bots/new?strategyId=${emaSummary.id}`);

    await userEvent.type(await screen.findByLabelText('Название'), 'Бот');
    await userEvent.type(screen.getByLabelText('fast'), '1');
    await userEvent.type(screen.getByLabelText('slow'), '30');
    await userEvent.type(screen.getByLabelText('Баланс бота'), '5');
    await userEvent.click(screen.getByRole('button', { name: 'Создать бота' }));

    expect(await screen.findByText('Не меньше 2')).toBeInTheDocument();
    expect(screen.getByLabelText('fast')).toHaveValue('1');
    expect(screen.getByLabelText('slow')).toHaveValue('30');
    expect(screen.getByLabelText('Название')).toHaveValue('Бот');
  });

  it('says the server cannot be reached when the request does not get an answer', async () => {
    fakeApi(
      routes({
        'POST /api/bots': () => {
          throw new TypeError('Failed to fetch');
        },
      }),
    );
    renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Баланс бота'), '5');
    await userEvent.click(await screen.findByRole('button', { name: 'Создать бота' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось связаться с сервером.');
  });

  it('leaves the form through the cancel link to the bot list', async () => {
    fakeApi({ ...routes(), 'GET /api/bots': () => json([]) });
    const { history } = renderApp('/bots/new');

    await userEvent.click(await screen.findByRole('link', { name: 'Отмена' }));

    await waitFor(() => expect(history.location.pathname).toBe('/bots'));
  });

  it('shows an error without a field above the buttons', async () => {
    fakeApi(routes({ 'POST /api/bots': () => problem(503, 'Биржа недоступна.') }));
    renderApp('/bots/new');

    await userEvent.type(await screen.findByLabelText('Баланс бота'), '5');
    await userEvent.click(await screen.findByRole('button', { name: 'Создать бота' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Биржа недоступна.');
  });
});

describe('bot form of an existing bot', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Смена интервала".
  it('changes the interval and shows it on the bot page', async () => {
    let bot = { ...botDetails };
    const requests = fakeApi(
      routes({
        [`GET /api/bots/${botId}`]: () => json(bot),
        [`PUT /api/bots/${botId}`]: () => {
          bot = { ...bot, interval: '240' };

          return new Response(null, { status: 204 });
        },
        [`GET /api/bots/${botId}/backtests`]: () => json([]),
        'GET /api/bots': () => json([]),
      }),
    );
    const { history } = renderApp(`/bots/${botId}/edit`);

    const interval = await screen.findByLabelText('Интервал');
    await waitFor(() => expect(interval).toHaveValue('60'));
    expect(screen.getByLabelText('fast')).toHaveValue('10');
    expect(await screen.findByText('EMA')).toBeInTheDocument();
    await userEvent.selectOptions(interval, '4 ч');
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/bots/${botId}`));
    expect(await screen.findByText(/· 4 ч ·/)).toBeInTheDocument();
    const put = requests.find((request) => request.method === 'PUT')!;
    expect(((await put.json()) as { interval: string; strategyId?: string }).interval).toBe('240');
    expect(within(document.body).queryByRole('combobox', { name: 'Стратегия' })).toBeNull();
  });

  it('leaves the form through the cancel link to the page of the bot', async () => {
    fakeApi(
      routes({
        [`GET /api/bots/${botId}`]: () => json(botDetails),
        [`GET /api/bots/${botId}/backtests`]: () => json([]),
      }),
    );
    const { history } = renderApp(`/bots/${botId}/edit`);

    await userEvent.click(await screen.findByRole('link', { name: 'Отмена' }));

    await waitFor(() => expect(history.location.pathname).toBe(`/bots/${botId}`));
  });

  it('says when the bot cannot be loaded', async () => {
    fakeApi(routes({ [`GET /api/bots/${botId}`]: () => problem(404, 'нет') }));
    renderApp(`/bots/${botId}/edit`);

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить бота.');
  });
});
