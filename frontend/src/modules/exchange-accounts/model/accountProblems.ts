import { ApiError } from '@/shared/api/client';
import { type FieldErrors, layOutProblems, type ProblemErrors } from '@/shared/api/problems';

const fields = new Set(['name', 'environment', 'apiKey', 'apiSecret']);

/** The errors of an account request by form field. */
export function accountFieldErrors(errors: ProblemErrors): FieldErrors {
  return layOutProblems(errors, (key) => fields.has(key));
}

/** What to tell the owner when a request about an account fails without field errors. */
export function accountProblem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Не удалось связаться с сервером.';
  }

  if (error.botNames.length > 0) {
    return `Аккаунт используют боты: ${error.botNames.join(', ')}.`;
  }

  if (error.status === 503) {
    return 'Bybit недоступен, попробуйте позже.';
  }

  return error.status === 502 ? `Bybit отклонил запрос: ${error.message}` : error.message;
}
