import { canCancel, canClose, statusLabel } from './status';

describe('status of a trade by an idea', () => {
  it('names every status the way the owner reads it', () => {
    expect(
      [
        'awaiting_entry',
        'open',
        'closed_at_target',
        'closed_at_stop',
        'closed_manually',
        'closed_at_expiry',
        'liquidated',
        'closed_by_adl',
        'cancelled',
        'failed',
      ].map(statusLabel),
    ).toEqual([
      'Ждёт входа',
      'Открыта',
      'Закрыта по цели',
      'Закрыта по стопу',
      'Закрыта вручную',
      'Закрыта по сроку',
      'Ликвидирована',
      'Закрыта биржей (ADL)',
      'Отменена',
      'Ошибка',
    ]);
    expect(statusLabel('new_status')).toBe('new_status');
  });

  it('cancels a trade that waits for its entry and closes one that is open, and no other', () => {
    expect(canCancel('awaiting_entry')).toBe(true);
    expect(canClose('open')).toBe(true);
    expect(canCancel('open')).toBe(false);
    expect(canClose('awaiting_entry')).toBe(false);
    expect(['failed', 'cancelled', 'closed_at_stop'].some((s) => canCancel(s) || canClose(s))).toBe(
      false,
    );
  });
});
