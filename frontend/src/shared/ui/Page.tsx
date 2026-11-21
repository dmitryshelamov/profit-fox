import type { ReactNode } from 'react';

/** The body of a page under the header: a column 1100 px wide in the middle. */
export function Page({ children }: { readonly children: ReactNode }) {
  return (
    <main className="mx-auto flex w-full max-w-[1148px] flex-col gap-10 px-6 pt-9 pb-16">
      {children}
    </main>
  );
}

/** The title of a page, 22 px bold, with a line under it and what stands on its right. */
export function PageTitle({
  title,
  hint,
  children,
}: {
  readonly title: string;
  readonly hint?: string;
  readonly children?: ReactNode;
}) {
  return (
    <header className="flex items-start justify-between gap-6">
      <div className="flex flex-col gap-1.5">
        <h1 className="text-[22px] leading-7 font-bold">{title}</h1>
        {hint !== undefined && <p className="text-muted">{hint}</p>}
      </div>
      {children !== undefined && <div className="flex items-center gap-3">{children}</div>}
    </header>
  );
}

/** The title of a section of a page, 16 px bold, with a count next to it when given. */
export function SectionTitle({
  title,
  hint,
  count,
}: {
  readonly title: string;
  readonly hint?: string;
  readonly count?: number;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <h2 className="flex items-center gap-2.5 text-base font-bold">
        {title}{' '}
        {count !== undefined && (
          <span className="rounded-[10px] bg-chip px-2 py-0.5 font-mono text-[11px] leading-none font-normal text-muted">
            {count}
          </span>
        )}
      </h2>
      {hint !== undefined && <p className="text-muted">{hint}</p>}
    </div>
  );
}
