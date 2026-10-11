import { MAP_W } from '../core/geo';
import { newId } from '../core/ids';
import { AddEntities, RemoveEntities } from '../commands/EntityCommands';
import { CompositeCommand, type Command } from '../commands/Command';
import type { StampEntity } from '../doc/types';
import type { Camera } from '../render/Camera';
import { strokePoints } from './brushes';
import type { PointerInput, Tool, ToolContext } from './Tool';

/**
 * Click to place the chosen asset; with scatter spacing > 0, drag to paint stamps along the
 * stroke (jittered inside the brush). Alt-drag erases stamps inside the brush. One undo step per
 * stroke; stamps appear live while painting.
 */
export class StampTool implements Tool {
  readonly id = 'stamp' as const;
  readonly cursor = 'copy';
  private stroke: { added: StampEntity[]; removed: StampEntity[]; last: { x: number; y: number }; carry: number; erase: boolean } | null = null;
  private cursorAt: PointerInput | null = null;
  private ctx: ToolContext | null = null;
  private seq = 1;

  activate(ctx: ToolContext): void { this.ctx = ctx; }

  down(p: PointerInput, ctx: ToolContext): void {
    this.stroke = { added: [], removed: [], last: { x: p.x, y: p.y }, carry: 0, erase: p.alt };
    if (p.alt) this.erase(p.x, p.y, ctx);
    else this.place(p.x, p.y, ctx, false);
  }

  move(p: PointerInput, ctx: ToolContext): void {
    this.cursorAt = p;
    const s = this.stroke;
    if (!s) { ctx.requestRender(); return; }
    const spacing = s.erase ? Math.max(1, ctx.options.radius * 0.3) : ctx.options.scatter;
    if (spacing <= 0) return;
    const { pts, carry } = strokePoints(s.last.x, s.last.y, p.x, p.y, spacing, s.carry);
    s.carry = carry; s.last = { x: p.x, y: p.y };
    for (const [x, y] of pts) { if (s.erase) this.erase(x, y, ctx); else this.place(x, y, ctx, true); }
  }

  up(_p: PointerInput, ctx: ToolContext): void {
    const s = this.stroke;
    this.stroke = null;
    if (!s || (!s.added.length && !s.removed.length)) return;
    // Effects are already applied; record a command that can undo/redo them.
    const parts: Command[] = [];
    if (s.added.length) parts.push(new AddEntities('Add stamps', ctx.doc.stamps, s.added));
    if (s.removed.length) parts.push(new RemoveEntities('Erase stamps', ctx.doc.stamps, s.removed.map((r) => r.id)));
    ctx.undo.record(new CompositeCommand(s.erase ? 'Erase stamps' : 'Place stamps', parts));
  }

  hover(p: PointerInput | null, ctx: ToolContext): void { this.cursorAt = p; ctx.requestRender(); }

  private place(x: number, y: number, ctx: ToolContext, scatter: boolean): void {
    const o = ctx.options;
    const variants = ctx.library.variants(o.asset);
    if (!variants) { ctx.status(`Unknown asset ${o.asset}`); return; }
    const r = scatter ? o.radius * 0.6 : 0;
    const a = Math.random() * Math.PI * 2, d = Math.sqrt(Math.random()) * r;
    const st: StampEntity = {
      id: newId('st'), asset: o.asset, variant: (this.seq++ * 2654435761 >>> 0) % variants,
      x: x + Math.cos(a) * d, y: y + Math.sin(a) * d, scale: o.stampScale * (scatter ? 0.85 + Math.random() * 0.3 : 1),
      rotation: 0, flip: scatter && Math.random() < 0.5,
    };
    ctx.doc.stamps.add([st]);
    this.stroke!.added.push(st);
  }

  private erase(x: number, y: number, ctx: ToolContext): void {
    const r2 = ctx.options.radius ** 2;
    const hit: StampEntity[] = [];
    for (const s of ctx.doc.stamps.all()) {
      let dx = s.x - x; dx -= Math.round(dx / MAP_W) * MAP_W;
      if (dx * dx + (s.y - y) ** 2 < r2) hit.push(s);
    }
    if (!hit.length) return;
    ctx.doc.stamps.remove(hit.map((h) => h.id));
    // A stamp added and erased in the same stroke simply never existed.
    const added = new Set(this.stroke!.added.map((a) => a.id));
    for (const h of hit) { if (added.has(h.id)) this.stroke!.added = this.stroke!.added.filter((a) => a.id !== h.id); else this.stroke!.removed.push(h); }
  }

  readonly gizmo = (g: CanvasRenderingContext2D, cam: Camera): void => {
    const p = this.cursorAt, ctx = this.ctx;
    if (!p || !ctx || (ctx.options.scatter <= 0 && !p.alt && !this.stroke?.erase)) return;
    const c = cam.toScreen(p.x, p.y);
    g.beginPath();
    g.arc(c.x, c.y, ctx.options.radius * (p.alt || this.stroke?.erase ? 1 : 0.6) * cam.zoom, 0, Math.PI * 2);
    g.strokeStyle = p.alt ? 'rgba(200,40,30,0.9)' : 'rgba(255,255,255,0.9)';
    g.lineWidth = 1.5; g.stroke();
  };
}
