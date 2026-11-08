const statuses: Record<string, string> = {
  awaiting_entry: 'Ждёт входа',
  open: 'Открыта',
  closed_at_target: 'Закрыта по цели',
  closed_at_stop: 'Закрыта по стопу',
  closed_manually: 'Закрыта вручную',
  closed_at_expiry: 'Закрыта по сроку',
  cancelled: 'Отменена',
  failed: 'Ошибка',
};

export const statusLabel = (code: string) => statuses[code] ?? code;

/** The entry of a trade is taken off only while it waits to be filled. */
export const canCancel = (status: string) => status === 'awaiting_entry';

/** An open trade is closed by the market. */
export const canClose = (status: string) => status === 'open';
