import { Badge } from '@/shared/ui/Badge';

/** Marks an account, or a bot of one, on the testnet; the main network needs no mark. */
export function EnvironmentBadge({ environment }: { environment: string }) {
  return environment === 'testnet' ? <Badge tone="warn">Testnet</Badge> : null;
}
