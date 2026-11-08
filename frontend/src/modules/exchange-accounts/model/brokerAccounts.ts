/** The labels of a brokerage account of T-Invest as the API codes it, and why one cannot be added. */
const types: Record<string, string> = { brokerage: 'Брокерский', iis: 'ИИС', other: 'Другой' };
const statuses: Record<string, string> = { open: 'открыт', new: 'новый', closed: 'закрыт' };
const problems: Record<string, string> = {
  not_open: 'закрыт',
  not_full_access: 'нет полного доступа',
  unsupported_type: 'не брокерский счёт и не ИИС',
};

export function brokerAccountType(code: string): string {
  return types[code] ?? code;
}

export function brokerAccountStatus(code: string): string {
  return statuses[code] ?? code;
}

/** Why the account cannot be added, or null for one that can. */
export function brokerAccountProblem(code: string | null): string | null {
  return code === null ? null : (problems[code] ?? code);
}
