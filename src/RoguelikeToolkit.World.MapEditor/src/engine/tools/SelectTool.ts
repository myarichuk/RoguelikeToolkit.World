import { MAP_W } from '../core/geo';
import { CompositeCommand } from '../commands/Command';
import { UpdateEntities } from '../commands/EntityCommands';
import type { LabelEntity, StampEntity } from '../doc/types';
import type { Camera } from '../render/Camera';
import { pickLabel, pickStamp, stampBounds } from './pick';
import type { PointerInput, Tool, ToolContext } from './Tool';

type Drag =
  | { kind: 'move'; start: PointerInput; stamps: StampEntity[]; labels: LabelEntity[] }
  | { kind: 'box'; start: PointerInput; cur: PointerInput };

/**
 * Select/transform stamps and labels. Click picks (labels first), Shift adds, drag moves, drag on
 * empty space box-selects. Keys: Delete, R/Shift+R rotate, [ ] scale, F flip, Esc clear.
 * Dragging updates entities live (no undo entries) and records one UpdateEntities on release.
 */
export class SelectTool implements Tool {
  readonly id = 'select' as const;
  readonly cursor = 'default';
  private drag: Drag | null = null;
  private ctx: ToolContext | null = null;

  activate(ctx: ToolContext): void { this.ctx = ctx; }

  down(p: PointerInput, ctx: ToolContext): void {
    const sel = ctx.selection.state;
    const theme = ctx.theme(), view = ctx.view();
    const labelId = view.layers.labels ? pickLabel(ctx.doc, ctx.labels, theme, p.x, p.y, () => true) : null;
    const stamp = !labelId && view.layers.stamps ? pickStamp(ctx.doc, ctx.library, p.x, p.y) : null;
    if (!labelId && !stamp) {
      if (!p.shift) ctx.selection.set({ stamps: new Set(), labels: new Set(), feature: null });
      this.drag = { kind: 'box', start: p, cur: p };
      return;
    }
    const stamps = new Set(sel.stamps), labels = new Set(sel.labels);
    const id = labelId ?? stamp!.id, set = labelId ? labels : stamps;
    const already = set.has(id);
    if (p.shift) { if (already) set.delete(id); else set.add(id); }
    else if (!already) { stamps.clear(); labels.clear(); set.add(id); }
    ctx.selection.set({ stamps, labels, feature: null });
    this.drag = {
      kind: 'move', start: p,
      stamps: [...stamps].map((s) => ctx.doc.stamps.get(s)!).filter(Boolean),
      labels: [...labels].map((l) => ctx.doc.labels.get(l)!).filter(Boolean),
    };
  }

  move(p: PointerInput, ctx: ToolContext): void {
    const d = this.drag;
    if (!d) return;
    if (d.kind === 'box') { d.cur = p; ctx.requestRender(); return; }
    const dx = p.x - d.start.x, dy = p.y - d.start.y;
    ctx.doc.stamps.update(d.stamps.map((s) => ({ ...s, x: s.x + dx, y: s.y + dy })));
    ctx.doc.labels.update(d.labels.map((l) => ({ ...l, x: l.x + dx, y: l.y + dy, path: l.path?.map((v, i) => v + (i % 2 ? dy : dx)) })));
    ctx.requestRender();
  }

  up(p: PointerInput, ctx: ToolContext): void {
    const d = this.drag;
    this.drag = null;
    if (!d) return;
    if (d.kind === 'box') { this.boxSelect(d.start, p, ctx); ctx.requestRender(); return; }
    if (Math.abs(p.x - d.start.x) + Math.abs(p.y - d.start.y) < 1e-6) return;
    const parts = [];
    if (d.stamps.length) parts.push(new UpdateEntities('Move stamps', ctx.doc.stamps, d.stamps, d.stamps.map((s) => ctx.doc.stamps.get(s.id)!)));
    if (d.labels.length) parts.push(new UpdateEntities('Move labels', ctx.doc.labels, d.labels, d.labels.map((l) => ctx.doc.labels.get(l.id)!)));
    ctx.undo.record(new CompositeCommand('Move', parts));
  }

  private boxSelect(a: PointerInput, b: PointerInput, ctx: ToolContext): void {
    const x0 = Math.min(a.x, b.x), x1 = Math.max(a.x, b.x), y0 = Math.min(a.y, b.y), y1 = Math.max(a.y, b.y);
    if (x1 - x0 < 0.5 && y1 - y0 < 0.5) return;
    const inside = (x: number, y: number) => {
      const q = x0 + ((((x - x0) % MAP_W) + MAP_W) % MAP_W);
      return q <= x1 && y >= y0 && y <= y1;
    };
    const stamps = new Set(a.shift ? ctx.selection.state.stamps : []);
    const labels = new Set(a.shift ? ctx.selection.state.labels : []);
    if (ctx.view().layers.stamps) for (const s of ctx.doc.stamps.all()) if (inside(s.x, s.y)) stamps.add(s.id);
    if (ctx.view().layers.labels) for (const l of ctx.doc.labels.all()) if (inside(l.x, l.y)) labels.add(l.id);
    ctx.selection.set({ stamps, labels });
    ctx.status(`${stamps.size} stamps, ${labels.size} labels selected`);
  }

  key(e: KeyboardEvent, ctx: ToolContext): boolean {
    const sel = ctx.selection.state;
    const stamps = [...sel.stamps].map((id) => ctx.doc.stamps.get(id)!).filter(Boolean);
    const labels = [...sel.labels].map((id) => ctx.doc.labels.get(id)!).filter(Boolean);
    if (!stamps.length && !labels.length) {
      // A feature picked with Inspect stays selected when switching tools; Delete still applies to it.
      if (sel.feature && (e.key === 'Delete' || e.key === 'Backspace')) { ctx.deleteSelected(); return true; }
      return false;
    }
    const upd = (label: string, fs: (s: StampEntity) => StampEntity, fl: (l: LabelEntity) => LabelEntity, key?: string) => {
      const parts = [];
      if (stamps.length) parts.push(new UpdateEntities(label, ctx.doc.stamps, stamps, stamps.map(fs), key && `${key}:s`));
      if (labels.length) parts.push(new UpdateEntities(label, ctx.doc.labels, labels, labels.map(fl), key && `${key}:l`));
      ctx.undo.execute(new CompositeCommand(label, parts));
    };
    switch (e.key) {
      case 'Delete': case 'Backspace':
        ctx.deleteSelected();
        return true;
      case 'r': case 'R': {
        const a = (e.shiftKey ? -1 : 1) * Math.PI / 24;
        upd('Rotate', (s) => ({ ...s, rotation: s.rotation + a }), (l) => ({ ...l, angle: l.angle + a }));
        return true;
      }
      case '[': case ']': {
        const k = e.key === ']' ? 1.1 : 1 / 1.1;
        upd('Scale', (s) => ({ ...s, scale: s.scale * k }), (l) => ({ ...l, size: (l.size || 6) * k }));
        return true;
      }
      case 'f': case 'F':
        upd('Flip', (s) => ({ ...s, flip: !s.flip }), (l) => ({ ...l, curve: -l.curve }));
        return true;
      case 'Escape':
        ctx.selection.set({ stamps: new Set(), labels: new Set() });
        return true;
    }
    return false;
  }

  readonly gizmo = (g: CanvasRenderingContext2D, cam: Camera): void => {
    const ctx = this.ctx;
    if (!ctx) return;
    g.strokeStyle = ctx.theme().ui?.['--accent'] ?? '#d08a2a';
    g.lineWidth = 1.5;
    g.setLineDash([5, 3]);
    for (const id of ctx.selection.state.stamps) {
      const s = ctx.doc.stamps.get(id);
      const b = s && stampBounds(s, ctx.library);
      if (!b) continue;
      for (const off of cam.copies(64)) {
        const a = cam.toScreen(b.x0 + off, b.y0), c = cam.toScreen(b.x1 + off, b.y1);
        g.strokeRect(a.x, a.y, c.x - a.x, c.y - a.y);
      }
    }
    if (this.drag?.kind === 'box') {
      const a = cam.toScreen(this.drag.start.x, this.drag.start.y), c = cam.toScreen(this.drag.cur.x, this.drag.cur.y);
      g.fillStyle = 'rgba(208,138,42,0.08)';
      g.fillRect(a.x, a.y, c.x - a.x, c.y - a.y);
      g.strokeRect(a.x, a.y, c.x - a.x, c.y - a.y);
    }
  };
}
