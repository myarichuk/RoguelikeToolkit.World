import type { ContentPack } from './types';

/**
 * Structural validation for packs from untrusted JSON. Hand-rolled (no schema dependency): it
 * checks what the editor relies on and reports every problem with a path, so a pack author sees
 * all mistakes at once. Unknown fields are allowed (forward compatibility).
 */
export function validatePack(raw: unknown): { pack?: ContentPack; errors: string[] } {
  const errors: string[] = [];
  const obj = (v: unknown, path: string): v is Record<string, unknown> => {
    if (v && typeof v === 'object' && !Array.isArray(v)) return true;
    errors.push(`${path}: expected an object`); return false;
  };
  const str = (v: unknown, path: string, optional = false) => {
    if (v === undefined && optional) return;
    if (typeof v !== 'string' || !v) errors.push(`${path}: expected a non-empty string`);
  };
  const num = (v: unknown, path: string, optional = false) => {
    if (v === undefined && optional) return;
    if (typeof v !== 'number' || !Number.isFinite(v)) errors.push(`${path}: expected a number`);
  };
  const arr = (v: unknown, path: string): v is unknown[] => {
    if (v === undefined) return false;
    if (Array.isArray(v)) return true;
    errors.push(`${path}: expected an array`); return false;
  };
  const color = (v: unknown, path: string) => {
    if (typeof v !== 'string' || !(v === 'none' || /^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i.test(v) || /^(rgb|hsl)a?\(/.test(v))) errors.push(`${path}: expected a color`);
  };

  if (!obj(raw, 'pack')) return { errors };
  str(raw.id, 'id'); str(raw.name, 'name'); str(raw.version, 'version');
  if (arr(raw.themes, 'themes')) raw.themes.forEach((t, i) => {
    const p = `themes[${i}]`;
    if (!obj(t, p)) return;
    str(t.id, `${p}.id`); str(t.name, `${p}.name`);
    if (obj(t.palette, `${p}.palette`)) for (const [k, v] of Object.entries(t.palette)) color(v, `${p}.palette.${k}`);
    if (obj(t.map, `${p}.map`)) for (const [k, v] of Object.entries(t.map)) (typeof v === 'number' ? num : color)(v, `${p}.map.${k}`);
    if (obj(t.biomes, `${p}.biomes`)) for (const [k, v] of Object.entries(t.biomes)) color(v, `${p}.biomes.${k}`);
    if (obj(t.labels, `${p}.labels`)) for (const [k, l] of Object.entries(t.labels)) {
      if (!obj(l, `${p}.labels.${k}`)) continue;
      str(l.font, `${p}.labels.${k}.font`); num(l.size, `${p}.labels.${k}.size`); color(l.fill, `${p}.labels.${k}.fill`);
    }
    if (t.stamps !== undefined && obj(t.stamps, `${p}.stamps`)) for (const [k, v] of Object.entries(t.stamps)) {
      if (!obj(v, `${p}.stamps.${k}`)) continue;
      num(v.density, `${p}.stamps.${k}.density`, true); num(v.scale, `${p}.stamps.${k}.scale`, true);
    }
  });
  if (arr(raw.biomes, 'biomes')) raw.biomes.forEach((b, i) => {
    if (!obj(b, `biomes[${i}]`)) return;
    num(b.id, `biomes[${i}].id`); str(b.name, `biomes[${i}].name`); color(b.vivid, `biomes[${i}].vivid`);
    if (typeof b.id === 'number' && (b.id < 0 || b.id > 255 || !Number.isInteger(b.id))) errors.push(`biomes[${i}].id: must be an integer 0..255`);
  });
  if (arr(raw.assets, 'assets')) raw.assets.forEach((a, i) => {
    const p = `assets[${i}]`;
    if (!obj(a, p)) return;
    str(a.id, `${p}.id`); str(a.name, `${p}.name`); str(a.category, `${p}.category`); num(a.size, `${p}.size`);
    if (!a.generator && !a.svg) errors.push(`${p}: needs either "generator" or "svg"`);
    str(a.generator, `${p}.generator`, true); str(a.svg, `${p}.svg`, true); num(a.variants, `${p}.variants`, true);
  });
  if (arr(raw.rules, 'rules')) raw.rules.forEach((r, i) => {
    const p = `rules[${i}]`;
    if (!obj(r, p)) return;
    str(r.id, `${p}.id`); num(r.spacing, `${p}.spacing`); num(r.density, `${p}.density`);
    if (arr(r.assets, `${p}.assets`)) r.assets.forEach((a, j) => str(a, `${p}.assets[${j}]`));
    else errors.push(`${p}.assets: required`);
    if (typeof r.spacing === 'number' && r.spacing < 1) errors.push(`${p}.spacing: must be >= 1 map unit`);
    if (r.layout !== undefined && r.layout !== 'grid' && r.layout !== 'relief') errors.push(`${p}.layout: "grid" or "relief"`);
    num(r.ridge, `${p}.ridge`, true); num(r.clump, `${p}.clump`, true);
    if (r.tiers !== undefined && arr(r.tiers, `${p}.tiers`)) r.tiers.forEach((t, j) => {
      if (!obj(t, `${p}.tiers[${j}]`)) return;
      num(t.above, `${p}.tiers[${j}].above`);
      if (arr(t.assets, `${p}.tiers[${j}].assets`)) t.assets.forEach((a, k) => str(a, `${p}.tiers[${j}].assets[${k}]`));
    });
  });
  if (raw.fonts !== undefined && obj(raw.fonts, 'fonts')) for (const [k, v] of Object.entries(raw.fonts)) str(v, `fonts.${k}`);
  return errors.length ? { errors } : { pack: raw as unknown as ContentPack, errors };
}
