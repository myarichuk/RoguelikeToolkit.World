import { RasterEdit } from '../commands/RasterEdit';
import type { Camera } from '../render/Camera';
import { biomeDab, climateDab, dabRect, heightDab, strokePoints } from './brushes';
import type { PointerInput, Tool, ToolContext } from './Tool';

/** Shared stroke handling: dabs at fixed spacing along the pointer path, one undo step per stroke. */
abstract class RasterBrush<T extends Float32Array | Uint8Array | Int8Array> implements Tool {
  abstract readonly id: 'terrain' | 'biome' | 'climate';
  readonly cursor = 'crosshair';
  protected edit: RasterEdit<T> | null = null;
  private last: { x: number; y: number } | null = null;
  private carry = 0;
  protected ctx: ToolContext | null = null;
  protected cursorAt: PointerInput | null = null;

  activate(ctx: ToolContext): void { this.ctx = ctx; }
  protected abstract begin(p: PointerInput, ctx: ToolContext): RasterEdit<T> | null;
  protected abstract dab(x: number, y: number, p: PointerInput, ctx: ToolContext): void;

  down(p: PointerInput, ctx: ToolContext): void {
    this.edit = this.begin(p, ctx);
    if (!this.edit) return;
    this.last = { x: p.x, y: p.y }; this.carry = 0;
    this.apply(p.x, p.y, p, ctx);
  }

  move(p: PointerInput, ctx: ToolContext): void {
    this.cursorAt = p;
    if (!this.edit || !this.last) { ctx.requestRender(); return; }
    const spacing = Math.max(0.5, ctx.options.radius * 0.22);
    const { pts, carry } = strokePoints(this.last.x, this.last.y, p.x, p.y, spacing, this.carry);
    this.carry = carry;
    for (const [x, y] of pts) this.apply(x, y, p, ctx);
    this.last = { x: p.x, y: p.y };
  }

  up(_p: PointerInput, ctx: ToolContext): void {
    if (this.edit?.commit()) ctx.undo.record(this.edit);
    this.edit = null; this.last = null;
  }

  hover(p: PointerInput | null, ctx: ToolContext): void { this.cursorAt = p; ctx.requestRender(); }

  private apply(x: number, y: number, p: PointerInput, ctx: ToolContext): void {
    this.edit!.prepare(dabRect({ x, y, radius: ctx.options.radius, strength: 0 }));
    this.dab(x, y, p, ctx);
  }

  readonly gizmo = (g: CanvasRenderingContext2D, cam: Camera): void => {
    if (!this.cursorAt || !this.ctx) return;
    const c = cam.toScreen(this.cursorAt.x, this.cursorAt.y);
    g.beginPath();
    g.arc(c.x, c.y, this.ctx.options.radius * cam.zoom, 0, Math.PI * 2);
    g.strokeStyle = 'rgba(0,0,0,0.6)'; g.lineWidth = 3; g.stroke();
    g.strokeStyle = 'rgba(255,255,255,0.9)'; g.lineWidth = 1.2; g.stroke();
  };
}

/** Raise / lower / smooth / flatten / roughen / paint land / paint sea. Alt inverts raise/lower. */
export class TerrainTool extends RasterBrush<Float32Array> {
  readonly id = 'terrain' as const;
  private target = 0;
  private seed = 1;
  protected begin(p: PointerInput, ctx: ToolContext): RasterEdit<Float32Array> {
    this.target = ctx.doc.elevationAt(p.x, p.y);
    this.seed = (this.seed * 1103515245 + 12345) >>> 0;
    return new RasterEdit(`Terrain: ${ctx.options.heightMode}`, ctx.doc.target('elevation'));
  }
  protected dab(x: number, y: number, p: PointerInput, ctx: ToolContext): void {
    const o = ctx.options;
    let mode = o.heightMode;
    if (p.alt && mode === 'raise') mode = 'lower'; else if (p.alt && mode === 'lower') mode = 'raise';
    const doc = ctx.doc;
    const rect = heightDab(doc.elevation, doc.width, doc.height, { x, y, radius: o.radius, strength: o.strength }, mode, this.target, this.seed);
    doc.target('elevation').touched(rect);
  }
}

/** Paints biome ids with organic edges. Alt picks the biome under the cursor. */
export class BiomeTool extends RasterBrush<Uint8Array> {
  readonly id = 'biome' as const;
  private seed = 7;
  protected begin(p: PointerInput, ctx: ToolContext): RasterEdit<Uint8Array> | null {
    if (p.alt) {
      ctx.setOptions({ biome: ctx.doc.biomeAt(p.x, p.y) });
      ctx.status(`Picked biome ${ctx.options.biome}`);
      return null;
    }
    this.seed = (this.seed * 1103515245 + 12345) >>> 0;
    return new RasterEdit('Paint biome', ctx.doc.target('biome'));
  }
  protected dab(x: number, y: number, _p: PointerInput, ctx: ToolContext): void {
    const o = ctx.options, doc = ctx.doc;
    const rect = biomeDab(doc.biome, doc.width, doc.height, { x, y, radius: o.radius, strength: Math.min(1, o.strength * 12) }, o.biome, this.seed++);
    doc.target('biome').touched(rect);
  }
}

/**
 * Paints a temperature or rainfall offset. Nothing about the terrain changes until the author applies
 * physics; then every tile takes the mean of the paint under it, added after the physical climate, and
 * biomes, rivers and lakes follow. Alt inverts warm/cool (wet/dry).
 */
export class ClimateTool extends RasterBrush<Int8Array> {
  readonly id = 'climate' as const;
  private layer: 'warmth' | 'wetness' = 'warmth';
  protected begin(_p: PointerInput, ctx: ToolContext): RasterEdit<Int8Array> | null {
    if (!ctx.doc.hasWorld) { ctx.status('Climate paint needs a generated world: generate one first.'); return null; }
    this.layer = ctx.options.climateLayer === 'temperature' ? 'warmth' : 'wetness';
    const what = this.layer === 'warmth' ? 'temperature' : 'rainfall';
    return new RasterEdit(`Climate: ${what} ${ctx.options.climateMode}`, ctx.doc.target(this.layer));
  }
  protected dab(x: number, y: number, p: PointerInput, ctx: ToolContext): void {
    const o = ctx.options, doc = ctx.doc;
    let mode = o.climateMode;
    if (p.alt && mode === 'raise') mode = 'lower'; else if (p.alt && mode === 'lower') mode = 'raise';
    const data = this.layer === 'warmth' ? doc.warmth : doc.wetness;
    const rect = climateDab(data, doc.width, doc.height, { x, y, radius: o.radius, strength: o.strength }, mode);
    doc.target(this.layer).touched(rect);
  }
}
