import { Badge } from './Badge';

/** The number of a version as the badge «v2»; a screen reader and a search by text get the words «версия 2». */
export function VersionBadge({ version }: { readonly version: number | string }) {
  return (
    <Badge mono>
      <span aria-hidden="true">v{String(version)}</span>
      <span className="sr-only">версия {String(version)}</span>
    </Badge>
  );
}
