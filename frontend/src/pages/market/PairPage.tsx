import { Page, PageTitle } from '@/shared/ui/Page';

/** The page of a pair (spec market-overview «Страница пары»). */
export function PairPage({ symbol }: { readonly symbol: string }) {
  return (
    <Page>
      <PageTitle title={symbol} />
    </Page>
  );
}
