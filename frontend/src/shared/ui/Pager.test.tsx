import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { Pager } from './Pager';

function show(page: number, overrides: { total?: number; pageSize?: number } = {}) {
  const onPage = vi.fn();
  const onPageSize = vi.fn();

  render(
    <Pager
      page={page}
      pageSize={overrides.pageSize ?? 50}
      total={overrides.total ?? 120}
      onPage={onPage}
      onPageSize={onPageSize}
    />,
  );

  return { onPage, onPageSize };
}

describe('the pager', () => {
  it('shows the first page with the previous button off', async () => {
    const { onPage } = show(1);

    expect(screen.getByText('1–50 из 120')).toBeInTheDocument();
    expect(screen.getByText('1 из 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Предыдущая страница' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Следующая страница' }));

    expect(onPage).toHaveBeenCalledWith(2);
  });

  it('turns both ways from a page in the middle', async () => {
    const { onPage } = show(2);

    expect(screen.getByText('51–100 из 120')).toBeInTheDocument();
    expect(screen.getByText('2 из 3')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Предыдущая страница' }));
    await userEvent.click(screen.getByRole('button', { name: 'Следующая страница' }));

    expect(onPage.mock.calls).toEqual([[1], [3]]);
  });

  it('ends the last page at the total with the next button off', () => {
    show(3);

    expect(screen.getByText('101–120 из 120')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Следующая страница' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Предыдущая страница' })).toBeEnabled();
  });

  it('groups the digits of a long list', () => {
    show(1, { total: 16503 });

    expect(screen.getByText('1–50 из 16 503')).toBeInTheDocument();
    expect(screen.getByText('1 из 331')).toBeInTheDocument();
  });

  it('offers the sizes 50, 100 and 200 and reports the one picked', async () => {
    const { onPageSize } = show(1, { pageSize: 100 });
    const size = screen.getByRole('combobox', { name: 'На странице' });

    expect(size).toHaveValue('100');
    expect(Array.from((size as HTMLSelectElement).options).map((option) => option.value)).toEqual([
      '50',
      '100',
      '200',
    ]);

    await userEvent.selectOptions(size, '200');

    expect(onPageSize).toHaveBeenCalledWith(200);
  });
});
