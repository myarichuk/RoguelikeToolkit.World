/**
 * One row of the engine's `HistoryPolityKeys()`, tab-separated:
 * `folk \t culture \t overlay \t displayName \t styleRace \t styleCulture \t architecture`.
 * Older engines send fewer fields (no `architecture`); missing fields read as empty, and an
 * empty `architecture` means the default look.
 */
export interface PolityKey {
  folk: string;
  culture: string;
  overlay: string;
  displayName: string;
  styleRace: string;
  styleCulture: string;
  /** Architectural style key from the culture's data ("dwarven", "sylvan", ...); '' = default. */
  architecture: string;
}

export function parsePolityKey(row: string | undefined): PolityKey {
  const [folk = '', culture = '', overlay = '', displayName = '', styleRace = '', styleCulture = '', architecture = ''] = (row ?? '').split('\t');
  return { folk, culture, overlay, displayName, styleRace, styleCulture, architecture: architecture.trim() };
}
