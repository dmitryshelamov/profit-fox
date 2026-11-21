import type { RequestError } from '@/api/client';

import { placeOf } from './preview';

/** The problems of a refused preview, each with where it is. */
export function ErrorList({ errors }: { readonly errors: readonly RequestError[] }) {
  return (
    <ul
      aria-label="Ошибки"
      className="flex flex-col gap-1 rounded-lg border border-err-bg bg-err-bg/60 p-3 text-xs"
    >
      {errors.map((error, index) => (
        <li key={index}>
          <span className="font-mono text-err">{placeOf(error)}</span>
          <span className="text-muted"> · {error.code} · </span>
          <span>{error.reason}</span>
        </li>
      ))}
    </ul>
  );
}
