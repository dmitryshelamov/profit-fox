import { ApiError, unwrap } from './client';

function response(status: number): Response {
  return new Response(null, { status });
}

describe('unwrap', () => {
  it('returns the data of a successful response', () => {
    expect(unwrap({ data: { id: 'a' }, response: response(200) })).toEqual({ id: 'a' });
  });

  it('throws the problem of a failed response with its errors, usages and bots', () => {
    const error = {
      status: 409,
      detail: 'Graphs that use the node would break.',
      errors: { name: ['Name must not be empty.'] },
      usages: [
        { kind: 'strategy', id: 's', name: 'EMA trend', errors: { 'graph.trend.period': 'x' } },
      ],
      bots: [
        { id: 'b', name: 'EMA BTC', errors: { 'parameterValues.fast': 'No such parameter.' } },
      ],
    };

    const thrown = (() => {
      try {
        unwrap({ error, response: response(409) });
      } catch (caught) {
        return caught;
      }
    })();

    expect(thrown).toBeInstanceOf(ApiError);
    expect(thrown).toMatchObject({
      status: 409,
      message: 'Graphs that use the node would break.',
      errors: { name: ['Name must not be empty.'] },
      usages: error.usages,
      bots: error.bots,
    });
  });

  it('describes a failed response without a body by its status', () => {
    expect(() =>
      unwrap({
        error: undefined,
        response: new Response(null, { status: 502, statusText: 'Bad Gateway' }),
      }),
    ).toThrow('Bad Gateway');
  });
});
