import { MAP_W, latOfY, lonOfX } from '../core/geo';
import type { Theme } from '../content/types';
import type { MapDocument } from '../doc/MapDocument';
import { labelExistsAt } from '../doc/existsAt';
import type { FeatureEntity, LabelEntity } from '../doc/types';
import type { LabelCache } from '../text/LabelCache';
import type { DepositType } from '../world/protocol';
import type { Camera } from './Camera';
import { CapacityTint } from './CapacityTint';
import { RealmLayer } from './RealmLayer';
import { ClimateTint } from './ClimateTint';
import { drawFlowArrows } from './FlowArrows';
import { ClimatePaint } from './ClimatePaint';
import type { ViewSettings } from './ViewSettings';

/** Color + short symbol per deposit type (chemical-symbol style markers). */
export const DEPOSIT_MARKERS: Record<DepositType, { color: string; symbol: string }> = {
  iron: { color: '#8a4b3a', symbol: 'Fe' }, copper: { color: '#b8693a', symbol: 'Cu' }, gold: { color: '#c9a227', symbol: 'Au' },
  silver: { color: '#9aa3ad', symbol: 'Ag' }, tin: { color: '#7d8c8f', symbol: 'Sn' }, leadZinc: { color: '#5f6670', symbol: 'Pb' },
  uranium: { color: '#6a9a3a', symbol: 'U' }, coal: { color: '#2d2a28', symbol: 'C' }, oil: { color: '#1f1f2a', symbol: 'Oil' },
  gas: { color: '#4f7fa8', symbol: 'Gas' }, salt: { color: '#c8c3b8', symbol: 'Na' }, gems: { color: '#8a3fa0', symbol: '◆' },
  bauxite: { color: '#a85a4a', symbol: 'Al' }, gypsum: { color: '#d8cf9c', symbol: 'CaSO₄' },
};

/** Something a tool wants drawn on top of the map this frame, in device pixels. */
export type GizmoPainter = (ctx: CanvasRenderingContext2D, cam: Camera) => void;

/**
 * Canvas 2D layer above the WebGL map: labels (vector glyph paths), resource markers, plate drift
 * arrows, selection/hover outlines and tool gizmos. Everything except gizmos is in map units and
 * drawn once per visible map copy with a single transform.
 */
export class Overlay {
  readonly ctx: CanvasRenderingContext2D;
  gizmos: GizmoPainter[] = [];
  /** The climate brush is active: show the paint whatever the layer toggle says. */
  showClimatePaint = false;
  readonly climatePaint = new ClimatePaint();
  private readonly capacityTint = new CapacityTint();
  private readonly realms = new RealmLayer();
  private readonly climateTint = new ClimateTint();
  selection: { labels: Set<string>; stamps: Set<string>; feature: string | null; hoverFeature: string | null } = { labels: new Set(), stamps: new Set(), feature: null, hoverFeature: null };
  private plateArrows: { x: number; y: number; dx: number; dy: number }[] | null = null;
  private plateArrowsFor: MapDocument | null = null;

  constructor(readonly canvas: HTMLCanvasElement, private readonly labels: LabelCache) {
    this.ctx = canvas.getContext('2d')!;
  }

  draw(cam: Camera, doc: MapDocument, theme: Theme, view: ViewSettings): void {
    const ctx = this.ctx;
    if (this.canvas.width !== cam.width || this.canvas.height !== cam.height) { this.canvas.width = cam.width; this.canvas.height = cam.height; }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, cam.width, cam.height);
    const vis = cam.visible();

    for (const off of cam.copies(256)) {
      const toScreen = () => ctx.setTransform(cam.zoom, 0, 0, cam.zoom, (off - cam.cx) * cam.zoom + cam.width / 2, -cam.cy * cam.zoom + cam.height / 2);
      toScreen();
      // River selection/hover: outline the river ribbon.
      for (const fid of [this.selection.hoverFeature, this.selection.feature]) {
        const f = fid ? doc.features.get(fid) : undefined;
        if (f?.riverId) this.strokeRiver(doc, f.riverId, theme, cam, fid === this.selection.feature);
      }
      if (view.layers.climate) this.drawClimate(doc, view);
      if (view.layers.capacity) this.drawCapacity(doc, view);
      if (view.layers.realms) this.realms.drawTint(ctx, doc, view.historyTurn);
      if (view.layers.borders) this.realms.drawBorders(ctx, doc, view.historyTurn, cam.zoom);
      if (view.layers.climatePaint || this.showClimatePaint) this.drawClimatePaint(doc);
      if (view.layers.labels) this.drawLabels(cam, doc, theme, view, off, vis);
      if (view.layers.resources && view.kinds.deposit) this.drawDeposits(cam, doc, view, off);
      if (view.layers.plateMotion) this.drawPlateMotion(cam, doc);
      if (view.layers.currents) drawFlowArrows(ctx, doc, 'current', cam.zoom, this.visibleIn(vis, off));
      if (view.layers.wind) drawFlowArrows(ctx, doc, 'wind', cam.zoom, this.visibleIn(vis, off));
      // Point-feature selection rings (settlements).
      for (const fid of [this.selection.hoverFeature, this.selection.feature]) {
        const f = fid ? doc.features.get(fid) : undefined;
        if (f && !f.tiles && f.kind !== 'deposit') this.ring(f.x, f.y, 6, cam, fid === this.selection.feature);
      }
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    for (const g of this.gizmos) { ctx.save(); g(ctx, cam); ctx.restore(); }
  }

  /** Labels visible under the current filters and zoom. */
  visibleLabel(l: LabelEntity, doc: MapDocument, theme: Theme, view: ViewSettings, zoom: number): boolean {
    const kind = l.feature?.kind;
    if (!labelExistsAt(doc, l.id, view.historyTurn)) return false;
    if (kind && kind in view.kinds && !view.kinds[kind as keyof typeof view.kinds]) return false;
    const style = theme.labels[l.style];
    return !style?.minZoom || zoom >= style.minZoom || this.selection.labels.has(l.id);
  }

  /** The visible rectangle in the coordinates of the map copy drawn at x offset `off`. */
  private visibleIn(vis: { x0: number; y0: number; x1: number; y1: number }, off: number): { x0: number; y0: number; x1: number; y1: number } {
    return { x0: vis.x0 - off, x1: vis.x1 - off, y0: vis.y0, y1: vis.y1 };
  }

  private drawClimate(doc: MapDocument, view: ViewSettings): void {
    const img = this.climateTint.current(doc, view.climateKind);
    if (!img) return;
    this.ctx.imageSmoothingEnabled = true;
    this.ctx.drawImage(img, 0, 0, doc.width, doc.height);
  }

  private drawCapacity(doc: MapDocument, view: ViewSettings): void {
    const img = this.capacityTint.current(doc, view.capacityKind);
    if (!img) return;
    this.ctx.imageSmoothingEnabled = true;
    this.ctx.drawImage(img, 0, 0, doc.width, doc.height);
  }

  private drawClimatePaint(doc: MapDocument): void {
    const img = this.climatePaint.current();
    if (!img) return;
    const ctx = this.ctx;
    ctx.imageSmoothingEnabled = true;
    ctx.drawImage(img, 0, 0, doc.width, doc.height);
  }

  private drawLabels(cam: Camera, doc: MapDocument, theme: Theme, view: ViewSettings, off: number, vis: { x0: number; y0: number; x1: number; y1: number }): void {
    const ctx = this.ctx;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    for (const l of doc.labels.all()) {
      if (!this.visibleLabel(l, doc, theme, view, cam.zoom)) continue;
      const r = this.labels.resolve(l, theme);
      if (!r) continue;
      const b = r.geo.bounds;
      if (b.x1 + off < vis.x0 || b.x0 + off > vis.x1 || b.y1 < vis.y0 || b.y0 > vis.y1) continue;
      const p = this.labels.path(r);
      if (r.style.haloWidth > 0) {
        ctx.strokeStyle = r.style.halo;
        ctx.lineWidth = r.style.haloWidth;
        ctx.globalAlpha = 0.85;
        ctx.stroke(p);
        ctx.globalAlpha = 1;
      }
      ctx.fillStyle = r.style.fill;
      ctx.fill(p);
      if (this.selection.labels.has(l.id)) {
        ctx.strokeStyle = theme.ui?.['--accent'] ?? '#d08a2a';
        ctx.lineWidth = 1.5 / cam.zoom;
        ctx.setLineDash([4 / cam.zoom, 3 / cam.zoom]);
        ctx.strokeRect(b.x0 - 1, b.y0 - 1, b.x1 - b.x0 + 2, b.y1 - b.y0 + 2);
        ctx.setLineDash([]);
      }
    }
  }

  private drawDeposits(cam: Camera, doc: MapDocument, view: ViewSettings, off: number): void {
    const ctx = this.ctx;
    const r = Math.max(2.2, 6 / cam.zoom);
    const vis = cam.visible();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const f of doc.features.all()) {
      if (f.kind !== 'deposit' || !f.deposit || !view.deposits[f.deposit.type]) continue;
      if (f.x + off < vis.x0 - r || f.x + off > vis.x1 + r || f.y < vis.y0 - r || f.y > vis.y1 + r) continue;
      const m = DEPOSIT_MARKERS[f.deposit.type];
      const rr = r * (0.75 + 0.5 * f.deposit.richness);
      ctx.beginPath();
      ctx.arc(f.x, f.y, rr, 0, Math.PI * 2);
      ctx.fillStyle = m.color; ctx.fill();
      ctx.lineWidth = Math.max(0.3, 1.2 / cam.zoom); ctx.strokeStyle = '#f4ecd8'; ctx.stroke();
      if (rr * cam.zoom > 7) {
        ctx.fillStyle = '#fff';
        ctx.font = `${(rr * 1.05).toFixed(2)}px system-ui, sans-serif`;
        ctx.fillText(m.symbol, f.x, f.y + rr * 0.05);
      }
      if (f.id === this.selection.feature || f.id === this.selection.hoverFeature) this.ring(f.x, f.y, rr + 1.5 / cam.zoom * 3, cam, f.id === this.selection.feature);
    }
  }

  private ring(x: number, y: number, rMu: number, cam: Camera, strong: boolean): void {
    const ctx = this.ctx;
    ctx.beginPath();
    ctx.arc(x, y, Math.max(rMu, 9 / cam.zoom), 0, Math.PI * 2);
    ctx.strokeStyle = strong ? '#e0902a' : 'rgba(224,144,42,0.7)';
    ctx.lineWidth = (strong ? 2.5 : 1.5) / cam.zoom;
    ctx.stroke();
  }

  /** A hollow casing around the river ribbon (the overlay is cleared inside it, so the drawn river shows through). */
  private strokeRiver(doc: MapDocument, riverId: string, theme: Theme, cam: Camera, strong: boolean): void {
    const r = doc.rivers.get(riverId);
    if (!r) return;
    const ctx = this.ctx, p = r.points;
    let w = 0;
    for (let i = 2; i < p.length; i += 3) w = Math.max(w, p[i]);
    const inner = Math.max(w * theme.map.riverWidth, 1.8 / cam.zoom) + 2 / cam.zoom;
    const outer = inner + ((strong ? 2.5 : 1.6) * 2) / cam.zoom;
    ctx.beginPath();
    for (let i = 0; i < p.length; i += 3) (i ? ctx.lineTo : ctx.moveTo).call(ctx, p[i], p[i + 1]);
    ctx.strokeStyle = theme.ui?.['--accent'] ?? '#e0902a';
    ctx.globalAlpha = strong ? 0.95 : 0.6;
    ctx.lineJoin = 'round'; ctx.lineCap = 'round';
    ctx.lineWidth = outer;
    ctx.stroke();
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = 'destination-out';
    ctx.lineWidth = inner;
    ctx.stroke();
    ctx.globalCompositeOperation = 'source-over';
  }

  private drawPlateMotion(cam: Camera, doc: MapDocument): void {
    if (this.plateArrowsFor !== doc) { this.plateArrows = plateArrows(doc); this.plateArrowsFor = doc; }
    const ctx = this.ctx;
    ctx.strokeStyle = '#1b1b1b'; ctx.fillStyle = '#1b1b1b';
    ctx.lineWidth = Math.max(0.6, 2 / cam.zoom);
    for (const a of this.plateArrows ?? []) {
      const ex = a.x + a.dx, ey = a.y + a.dy;
      ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(ex, ey); ctx.stroke();
      const ang = Math.atan2(a.dy, a.dx), h = Math.max(6, 12 / cam.zoom);
      ctx.beginPath();
      ctx.moveTo(ex, ey);
      ctx.lineTo(ex - h * Math.cos(ang - 0.4), ey - h * Math.sin(ang - 0.4));
      ctx.lineTo(ex - h * Math.cos(ang + 0.4), ey - h * Math.sin(ang + 0.4));
      ctx.closePath(); ctx.fill();
      ctx.beginPath(); ctx.arc(a.x, a.y, ctx.lineWidth * 1.6, 0, Math.PI * 2); ctx.fill();
    }
  }

  /** Feature under the overlay's point markers (deposits, settlements), for picking. */
  static pointFeatureAt(doc: MapDocument, x: number, y: number, radiusMu: number, accept: (f: FeatureEntity) => boolean): FeatureEntity | null {
    let best: FeatureEntity | null = null, bd = radiusMu * radiusMu;
    for (const f of doc.features.all()) {
      if (f.tiles && f.kind !== 'river') continue;
      if (f.kind === 'river' || !accept(f)) continue;
      let dx = f.x - x; dx -= Math.round(dx / MAP_W) * MAP_W;
      const d = dx * dx + (f.y - y) ** 2;
      if (d < bd) { bd = d; best = f; }
    }
    return best;
  }
}

/** Plate centroids (circular mean in x) and drift projected onto the local east/north plane. */
function plateArrows(doc: MapDocument): { x: number; y: number; dx: number; dy: number }[] {
  const { tilePlate, tileXY, plateDrift } = doc.world;
  const acc = new Map<number, { s: number; c: number; y: number; n: number }>();
  for (let t = 0; t < tilePlate.length; t++) {
    const id = tilePlate[t] & 0xffff;
    if (!id) continue;
    const a = (tileXY[t * 2] / MAP_W) * Math.PI * 2;
    let e = acc.get(id); if (!e) acc.set(id, (e = { s: 0, c: 0, y: 0, n: 0 }));
    e.s += Math.sin(a); e.c += Math.cos(a); e.y += tileXY[t * 2 + 1]; e.n++;
  }
  const out: { x: number; y: number; dx: number; dy: number }[] = [];
  for (const [id, e] of acc) {
    const x = ((Math.atan2(e.s, e.c) / (Math.PI * 2)) * MAP_W + MAP_W) % MAP_W, y = e.y / e.n;
    const i = (id - 1) * 4;
    if (i + 3 >= plateDrift.length) continue;
    const lon = lonOfX(x), lat = latOfY(y);
    const [vx, vy, vz, speed] = [plateDrift[i], plateDrift[i + 1], plateDrift[i + 2], plateDrift[i + 3]];
    const east = -Math.sin(lon) * vx + Math.cos(lon) * vy;
    const north = -Math.sin(lat) * Math.cos(lon) * vx - Math.sin(lat) * Math.sin(lon) * vy + Math.cos(lat) * vz;
    const len = Math.hypot(east, north) || 1;
    const L = 30 + 60 * Math.min(1, Math.abs(speed));
    out.push({ x, y, dx: (east / len) * L, dy: (-north / len) * L });
  }
  return out;
}
