import { Page, PageTitle } from '@/shared/ui/Page';

/** «Идеи» (spec strategy-ideas «Раздел «Идеи»»). */
export function IdeasPage() {
  return (
    <Page>
      <PageTitle
        title="Идеи"
        hint="Включённые стратегии на всех бессрочных парах. Итог идеи считается по её сроку, стопу или цели."
      />
    </Page>
  );
}
