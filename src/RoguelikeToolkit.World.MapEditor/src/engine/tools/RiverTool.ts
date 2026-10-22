import { newId } from '../core/ids';
import { AddEntities, RemoveEntities } from '../commands/EntityCommands';
import { CompositeCommand, type Command } from '../commands/Command';
import { labelIdOf, type FeatureEntity, type LabelEntity, type RiverEntity } from '../doc/types';
import type { Camera } from '../render/Camera';
import { pickFeature } from './pick';
import type { PointerInput, Tool, ToolContext } from './Tool';

/**
 * Draw a river from source to mouth: it widens downstream, gets smoothed, becomes a selectable
 * feature with a culture-appropriate name, and gets a label along its course. Alt-click deletes a
 * river together with its feature and label.
 */
export class RiverTool implements Tool {
  readonly id = 'river' as const;
  readonly cursor = 'crosshair';
  private pts: [number, number][] = [];
  private drawing = false;

  down(p: PointerInput, ctx: ToolContext): void {
    if (p.alt) { this.remove(p, ctx); return; }
    this.drawing = true;
    this.pts = [[p.x, p.y]];
  }

  move(p: PointerInput, ctx: ToolContext): void {
    if (!this.drawing) return;
    const [lx, ly] = this.pts[this.pts.length - 1];
    if (Math.hypot(p.x - lx, p.y - ly) >= 1.5) { this.pts.push([p.x, p.y]); ctx.requestRender(); }
  }

  async up(_p: PointerInput, ctx: ToolContext): Promise<void> {
    if (!this.drawing) return;
    this.drawing = false;
    const raw = this.pts;
    this.pts = [];
    if (raw.length < 4) return;
    const smooth = chaikin(chaikin(raw));
    const n = smooth.length;
    const points: number[] = [];
    smooth.forEach(([x, y], i) => points.push(Math.round(x * 100) / 100, Math.round(y * 100) / 100, Math.round((0.25 + 1.4 * Math.pow(i / (n - 1), 0.8)) * 100) / 100));
    const mid = smooth[Math.floor(n * 0.5)];
    let name = '';
    try { name = await ctx.nameAt('river', mid[0], mid[1]); } catch { /* unnamed */ }
    const river: RiverEntity = { id: newId('river'), name, points };
    const feature: FeatureEntity = {
      id: newId('f_river'), kind: 'river', name, x: mid[0], y: mid[1], angle: 0, extent: 0, weight: n, riverId: river.id,
      details: [['Origin', 'Drawn by hand'], ['Length', `${Math.round(pathLength(smooth))} map units`]], culture: '',
    };
    const cmds: Command[] = [new AddEntities('Add river', ctx.doc.rivers, [river]), new AddEntities('Add feature', ctx.doc.features, [feature])];
    if (name) {
      const span = smooth.slice(Math.floor(n * 0.3), Math.ceil(n * 0.7)).flat();
      const label: LabelEntity = { id: newId('lb'), text: name, style: 'river', x: mid[0], y: mid[1], angle: 0, size: 0, spacing: NaN, curve: 0, path: span, feature: { kind: 'river' } };
      cmds.push(new AddEntities('Add label', ctx.doc.labels, [label]));
    }
    ctx.undo.execute(new CompositeCommand('Draw river', cmds));
    ctx.selection.set({ feature: feature.id, stamps: new Set(), labels: new Set() });
  }

  private remove(p: PointerInput, ctx: ToolContext): void {
    const f = pickFeature(ctx.doc, p.x, p.y, ctx.camera.zoom, { ...ctx.view(), kinds: { ...ctx.view().kinds, river: true } });
    if (!f || f.kind !== 'river') return;
    const labels = [...ctx.doc.labels.all()].filter((l) => l.id === labelIdOf(f.id) || (l.feature?.kind === 'river' && l.text === f.name)).map((l) => l.id);
    ctx.undo.execute(new CompositeCommand('Delete river', [
      new RemoveEntities('Delete label', ctx.doc.labels, labels),
      new RemoveEntities('Delete feature', ctx.doc.features, [f.id]),
      ...(f.riverId ? [new RemoveEntities('Delete river', ctx.doc.rivers, [f.riverId])] : []),
    ]));
    ctx.selection.set({ feature: null });
  }

  readonly gizmo = (g: CanvasRenderingContext2D, cam: Camera): void => {
    if (this.pts.length < 2) return;
    g.beginPath();
    this.pts.forEach(([x, y], i) => { const s = cam.toScreen(x, y); if (i) g.lineTo(s.x, s.y); else g.moveTo(s.x, s.y); });
    g.strokeStyle = 'rgba(40,90,140,0.9)'; g.lineWidth = 2.5; g.lineCap = 'round'; g.lineJoin = 'round';
    g.stroke();
  };
}

function chaikin(p: [number, number][]): [number, number][] {
  const out: [number, number][] = [p[0]];
  for (let i = 0; i < p.length - 1; i++) {
    const [ax, ay] = p[i], [bx, by] = p[i + 1];
    out.push([0.75 * ax + 0.25 * bx, 0.75 * ay + 0.25 * by], [0.25 * ax + 0.75 * bx, 0.25 * ay + 0.75 * by]);
  }
  out.push(p[p.length - 1]);
  return out;
}

const pathLength = (p: [number, number][]) => p.reduce((s, q, i) => (i ? s + Math.hypot(q[0] - p[i - 1][0], q[1] - p[i - 1][1]) : 0), 0);
