import type { LabelStyle, Theme } from '../content/types';
import type { LabelEntity } from '../doc/types';
import type { FontLibrary } from './FontLibrary';
import { layoutLabel, type LabelGeometry } from './LabelLayout';

export interface ResolvedLabel { label: LabelEntity; style: LabelStyle; geo: LabelGeometry; path: Path2D | null; }

/**
 * Label geometry is pure in (label fields, style, font). This cache keeps one layout per label
 * keyed by a cheap signature, so panning/zooming never re-lays text out and edits re-lay only
 * the edited label. Path2D objects are created lazily (screen only; export uses `geo.d`).
 */
export class LabelCache {
  private cache = new Map<string, { sig: string; r: ResolvedLabel }>();

  constructor(private readonly fonts: FontLibrary) {}

  styleFor(theme: Theme, label: LabelEntity): LabelStyle {
    return theme.labels[label.style] ?? theme.labels.town ?? Object.values(theme.labels)[0];
  }

  resolve(label: LabelEntity, theme: Theme): ResolvedLabel | null {
    const style = this.styleFor(theme, label);
    if (!style) return null;
    const font = this.fonts.get(style.font);
    if (!font) return null;
    const size = label.size || style.size;
    const spacing = Number.isNaN(label.spacing) ? style.spacing : label.spacing;
    const sig = `${this.fonts.version}|${style.font}|${style.uppercase}|${size}|${spacing}|${label.text}|${label.x}|${label.y}|${label.angle}|${label.curve}|${label.path?.length ?? 0}:${label.path?.[0] ?? ''}`;
    const hit = this.cache.get(label.id);
    if (hit && hit.sig === sig) { hit.r.style = style; hit.r.label = label; return hit.r; }
    const geo = layoutLabel(font, { text: label.text, size, spacing, uppercase: style.uppercase, x: label.x, y: label.y, angle: label.angle, curve: label.curve, path: label.path });
    const r: ResolvedLabel = { label, style, geo, path: null };
    this.cache.set(label.id, { sig, r });
    return r;
  }

  path(r: ResolvedLabel): Path2D {
    return (r.path ??= new Path2D(r.geo.d));
  }

  forget(id: string): void { this.cache.delete(id); }
  clear(): void { this.cache.clear(); }
}
