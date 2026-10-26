import { Emitter } from '../core/Emitter';
import type { AssetDef, BiomeDef, ContentPack, PlacementRule, Theme } from './types';
import { validatePack } from './validate';

interface Loaded { pack: ContentPack; baseUrl: string; enabled: boolean; }

export const USER_PACK_ID = 'user';

/**
 * Layered content: packs in load order, later ones override earlier ones by id. A user overlay
 * pack (id "user") always sits on top and receives in-editor edits (theme tweaks), so edits are
 * non-destructive, removable, and exportable as an ordinary pack.
 */
export class PackRegistry {
  private loaded: Loaded[] = [];
  readonly events = new Emitter<{ change: PackRegistry }>();
  /** Bumps on change; cache key for derived data (atlas, palettes). */
  version = 0;

  get packs(): readonly Loaded[] { return this.loaded; }

  /** Adds (or replaces, by id) a pack. Throws with all validation errors. */
  add(raw: unknown, baseUrl: string): ContentPack {
    const { pack, errors } = validatePack(raw, new Set(this.loaded.flatMap((l) => (l.pack.assets ?? []).map((a) => a.id))));
    if (!pack) throw new Error(`Invalid content pack:\n${errors.join('\n')}`);
    const existing = this.loaded.findIndex((l) => l.pack.id === pack.id);
    const entry = { pack, baseUrl, enabled: true };
    if (existing >= 0) this.loaded[existing] = entry;
    else {
      // Keep the user overlay last.
      const userIdx = this.loaded.findIndex((l) => l.pack.id === USER_PACK_ID);
      if (userIdx >= 0 && pack.id !== USER_PACK_ID) this.loaded.splice(userIdx, 0, entry); else this.loaded.push(entry);
    }
    this.changed();
    return pack;
  }

  remove(id: string): void {
    this.loaded = this.loaded.filter((l) => l.pack.id !== id);
    this.changed();
  }

  setEnabled(id: string, enabled: boolean): void {
    const l = this.loaded.find((x) => x.pack.id === id);
    if (l && l.enabled !== enabled) { l.enabled = enabled; this.changed(); }
  }

  private active(): Loaded[] { return this.loaded.filter((l) => l.enabled); }

  private merged<T extends { id: string | number }>(pick: (p: ContentPack) => T[] | undefined): T[] {
    const map = new Map<string | number, T>();
    for (const l of this.active()) for (const item of pick(l.pack) ?? []) map.set(item.id, item);
    return [...map.values()];
  }

  themes(): Theme[] { return this.merged((p) => p.themes); }
  theme(id: string): Theme { return this.themes().find((t) => t.id === id) ?? this.themes()[0]; }
  biomes(): BiomeDef[] { return this.merged((p) => p.biomes).sort((a, b) => a.id - b.id); }
  rules(): PlacementRule[] { return this.merged((p) => p.rules); }

  assets(): Map<string, AssetDef> {
    return new Map(this.merged((p) => p.assets).map((a) => [a.id, a]));
  }

  /**
   * Marker assets for a feature kind; with an architectural style, that style's assets for the kind
   * (later packs win), else the kind's plain `featureAssets`.
   */
  featureAssets(kind: string, architecture?: string): string[] {
    if (architecture) {
      let styled: string[] | undefined;
      for (const l of this.active()) styled = l.pack.architecture?.[architecture]?.[kind] ?? styled;
      if (styled?.length) return styled;
    }
    let out: string[] = [];
    for (const l of this.active()) out = l.pack.featureAssets?.[kind] ?? out;
    return out;
  }

  /** Architectural style keys the active packs define. */
  architectures(): string[] {
    const out = new Set<string>();
    for (const l of this.active()) for (const k of Object.keys(l.pack.architecture ?? {})) out.add(k);
    return [...out];
  }

  /** Font key -> absolute URL. */
  fonts(): Record<string, string> {
    const out: Record<string, string> = {};
    for (const l of this.active()) for (const [k, v] of Object.entries(l.pack.fonts ?? {})) out[k] = new URL(v, l.baseUrl).href;
    return out;
  }

  /** Writes a theme into the user overlay (creating it on first edit). */
  putTheme(theme: Theme): void {
    let user = this.loaded.find((l) => l.pack.id === USER_PACK_ID);
    if (!user) {
      user = { pack: { id: USER_PACK_ID, name: 'My edits', version: '1.0.0', themes: [] }, baseUrl: location.href, enabled: true };
      this.loaded.push(user);
    }
    const themes = user.pack.themes ?? (user.pack.themes = []);
    const i = themes.findIndex((t) => t.id === theme.id);
    if (i >= 0) themes[i] = theme; else themes.push(theme);
    this.changed();
  }

  /** Drops a theme override from the user overlay (back to the pack's original). */
  resetTheme(id: string): void {
    const themes = this.loaded.find((l) => l.pack.id === USER_PACK_ID)?.pack.themes;
    const i = themes?.findIndex((t) => t.id === id) ?? -1;
    if (i >= 0) { themes!.splice(i, 1); this.changed(); }
  }

  /** True when the user overlay overrides this theme. */
  isEdited(id: string): boolean {
    return !!this.loaded.find((l) => l.pack.id === USER_PACK_ID)?.pack.themes?.some((t) => t.id === id);
  }

  /** The user overlay as a standalone pack (for "export my theme"). */
  userPack(): ContentPack | undefined {
    return this.loaded.find((l) => l.pack.id === USER_PACK_ID)?.pack;
  }

  private changed(): void { this.version++; this.events.emit('change', this); }
}
