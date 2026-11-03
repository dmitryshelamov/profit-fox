import { Badge } from '@/shared/ui/Badge';

const LABELS: Record<string, string> = {
  stopped: 'Остановлен',
  running: 'Запущен',
  paused: 'Пауза',
  stopping: 'Останавливается',
  failed: 'Ошибка',
};

const DOTS: Record<string, string> = {
  running: '#3fb97f',
  paused: '#d9a441',
  stopping: '#d9a441',
  failed: '#e5534b',
};

/** Where a bot is in live trading. */
export function LiveStatusBadge({ status }: { status: string }) {
  return (
    <Badge tone={status === 'failed' ? 'warn' : 'neutral'} dot={DOTS[status]}>
      {LABELS[status] ?? status}
    </Badge>
  );
}
