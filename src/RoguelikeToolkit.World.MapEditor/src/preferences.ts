/** The few per-browser conveniences the editor remembers (the map itself is kept by the SessionController). Storage may be unavailable, so every access degrades to "not set". */
export type PreferenceKey = 'tab' | 'wizard' | 'msPerTile';

const slot = (key: PreferenceKey): string => `wme.${key}`;

export function getPreference(key: PreferenceKey): string | null {
  try { return localStorage.getItem(slot(key)); } catch { return null; }
}

export function setPreference(key: PreferenceKey, value: string): void {
  try { localStorage.setItem(slot(key), value); } catch { /* storage unavailable */ }
}
