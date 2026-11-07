import { ideaFeatures } from '../model/features';

/** The chips of the features of the market an idea fired with, in a fixed order, the name with its bound on hover; nothing without one. */
export function IdeaFeatureChips({ features }: { features: readonly string[] }) {
  const present = ideaFeatures.filter((feature) => features.includes(feature.code));

  if (present.length === 0) {
    return null;
  }

  return (
    <span className="ml-1 inline-flex flex-wrap gap-1 align-middle">
      {present.map((feature) => (
        <span
          key={feature.code}
          title={feature.hint}
          className="rounded bg-panel-2 px-1.5 py-0.5 text-xs whitespace-nowrap text-muted"
        >
          {feature.chip}
        </span>
      ))}
    </span>
  );
}
