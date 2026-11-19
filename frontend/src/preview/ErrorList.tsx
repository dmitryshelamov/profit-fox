import type { RequestError } from '@/api/client';

import { placeOf } from './preview';

/** The problems of a refused preview, each with where it is. */
export function ErrorList({ errors }: { readonly errors: readonly RequestError[] }) {
  return (
    <ul
      aria-label="Ошибки"
      className="space-y-1 rounded border border-red-900 bg-red-950/40 p-3 text-sm"
    >
      {errors.map((error, index) => (
        <li key={index}>
          <span className="font-mono text-red-300">{placeOf(error)}</span>
          <span className="text-zinc-400"> · {error.code} · </span>
          <span>{error.reason}</span>
        </li>
      ))}
    </ul>
  );
}
