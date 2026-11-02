import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { BotForm, botQuery } from '@/modules/bots';

export function BotEditorPage({ botId, strategyId }: { botId?: string; strategyId?: string }) {
  const bot = useQuery({ ...botQuery(botId ?? ''), enabled: botId !== undefined });
  const title = botId ? 'Правка бота' : 'Новый бот';

  return (
    <section className="space-y-5">
      <header className="space-y-2">
        <p className="text-xs text-muted">
          <Link to="/bots" className="hover:underline">
            Боты
          </Link>{' '}
          / {title}
        </p>
        <h1 className="text-2xl font-bold">{title}</h1>
      </header>
      {bot.isError && <p role="alert">Не удалось загрузить бота.</p>}
      {botId === undefined && <BotForm strategyId={strategyId} />}
      {bot.data && <BotForm bot={bot.data} />}
    </section>
  );
}
