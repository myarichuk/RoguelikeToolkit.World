import type { MapDocument } from '../doc/MapDocument';
import { decodeBattles, outcomeColor, type Battle } from '../world/history/battles';
import type { EventRecord, ExpansionRecord } from '../world/history/record';
import { buildRoads } from '../world/history/roads';
import { outlawHavens, havensAt, raidsAt, routeKey, routeSafetyAt, safetyColor, type Haven } from '../world/history/routes';
import type { ViewSettings } from './ViewSettings';

/** What the marks need from a record, decoded once per expansion record: the routes as the roads layer draws them. */
interface Decoded { x: ExpansionRecord; battles: Battle[]; havens: Haven[]; routes: { key: string; points: Float32Array }[]; }

/**
 * The history expansion on the map at the scrubbed turn: battle markers coloured by outcome, rings on besieged places,
 * trade roads coloured by safety, outlaw havens and caravan-raid pins. Drawn by the overlay in map units, one map copy at a
 * time; a world without expansion records draws nothing.
 */
export class HistoryMarks {
  private decoded: Decoded | null = null;

  private of(doc: MapDocument): Decoded | null {
    const x = doc.history?.expansion;
    if (!x) return null;
    if (this.decoded?.x !== x) {
      // The same wander, smoothing and water cuts as the roads the worker built, so the heat lies on the drawn roads.
      const xy = doc.world.tileXY;
      const routes = buildRoads({ segments: x.roads, seed: doc.settings.seed, xy: (t) => [xy[t * 2], xy[t * 2 + 1]], wet: (px, py) => doc.elevationAt(px, py) < 0 })
        .map((r) => ({ key: routeKey(r.from, r.to), points: r.points }));
      this.decoded = { x, battles: decodeBattles(x), havens: outlawHavens(doc.history!.events), routes };
    }
    return this.decoded;
  }

  draw(ctx: CanvasRenderingContext2D, doc: MapDocument, view: ViewSettings, zoom: number): void {
    const d = this.of(doc), h = doc.history;
    if (!d || !h) return;
    const turn = view.historyTurn < 0 ? h.turns : Math.min(view.historyTurn, h.turns);
    const xy = doc.world.tileXY;
    const at = (tile: number): [number, number] | null => (tile >= 0 && tile * 2 + 1 < xy.length ? [xy[tile * 2], xy[tile * 2 + 1]] : null);
    const px = (n: number) => n / zoom;
    ctx.save();
    ctx.lineJoin = 'round'; ctx.lineCap = 'round';
    if (view.layers.routeSafety) this.drawRoutes(ctx, d, turn, px);
    if (view.layers.outlaws) this.drawOutlaws(ctx, d, h.events, turn, at, px);
    if (view.layers.battles) this.drawBattles(ctx, d, turn, at, px);
    ctx.restore();
  }

  private drawRoutes(ctx: CanvasRenderingContext2D, d: Decoded, turn: number, px: (n: number) => number): void {
    const safety = routeSafetyAt(d.x.routeSeries, turn);
    ctx.lineWidth = Math.max(1.4, px(4));
    for (const r of d.routes) {
      const s = safety.get(r.key), p = r.points;
      if (!s) continue;
      ctx.strokeStyle = safetyColor(s.safety, 0.85);
      ctx.beginPath();
      for (let i = 0; i < p.length; i += 3) (i ? ctx.lineTo : ctx.moveTo).call(ctx, p[i], p[i + 1]);
      ctx.stroke();
    }
  }

  private drawOutlaws(ctx: CanvasRenderingContext2D, d: Decoded, events: readonly EventRecord[], turn: number, at: (t: number) => [number, number] | null, px: (n: number) => number): void {
    const r = Math.max(2.5, px(7));
    for (const haven of havensAt(d.havens, turn)) {
      const p = at(haven.tile);
      if (!p) continue;
      // A camp: a dark tent with a red rim.
      ctx.beginPath();
      ctx.moveTo(p[0], p[1] - r); ctx.lineTo(p[0] + r, p[1] + r * 0.8); ctx.lineTo(p[0] - r, p[1] + r * 0.8); ctx.closePath();
      ctx.fillStyle = '#2b2222'; ctx.fill();
      ctx.strokeStyle = '#c0392b'; ctx.lineWidth = Math.max(0.4, px(1.5)); ctx.stroke();
    }
    for (const raid of raidsAt(events, turn)) {
      const p = at(raid.tile);
      if (!p) continue;
      // A pin: a head on a short stem, its point on the road.
      ctx.beginPath();
      ctx.moveTo(p[0], p[1]); ctx.lineTo(p[0], p[1] - r * 1.6);
      ctx.strokeStyle = '#3a1010'; ctx.lineWidth = Math.max(0.4, px(1.5)); ctx.stroke();
      ctx.beginPath(); ctx.arc(p[0], p[1] - r * 1.6, r * 0.6, 0, Math.PI * 2);
      ctx.fillStyle = raid.band >= 0 ? '#c0392b' : '#8e5a2b'; ctx.fill();
      ctx.strokeStyle = '#f4ecd8'; ctx.stroke();
    }
  }

  private drawBattles(ctx: CanvasRenderingContext2D, d: Decoded, turn: number, at: (t: number) => [number, number] | null, px: (n: number) => number): void {
    const r = Math.max(2.5, px(7));
    for (const b of d.battles) {
      if (b.turn !== turn) continue;
      const colour = outcomeColor(b.outcome);
      if (b.kind === 'Siege') {
        // The ring sits on the besieged place, dashed while it held, solid where it fell.
        const p = at(b.settlement >= 0 ? d.x.settlementTiles[b.settlement] ?? -1 : b.tile);
        if (!p) continue;
        ctx.beginPath(); ctx.arc(p[0], p[1], r * 1.8, 0, Math.PI * 2);
        ctx.strokeStyle = colour; ctx.lineWidth = Math.max(0.6, px(3));
        ctx.setLineDash(b.outcome.startsWith('Attacker') ? [] : [px(5), px(4)]);
        ctx.stroke();
        ctx.setLineDash([]);
        continue;
      }
      const p = at(b.tile);
      if (!p) continue;
      // A field: a diamond in the outcome's colour, crossed like two blades.
      ctx.beginPath();
      ctx.moveTo(p[0], p[1] - r); ctx.lineTo(p[0] + r, p[1]); ctx.lineTo(p[0], p[1] + r); ctx.lineTo(p[0] - r, p[1]); ctx.closePath();
      ctx.fillStyle = colour; ctx.fill();
      ctx.strokeStyle = '#f4ecd8'; ctx.lineWidth = Math.max(0.4, px(1.5)); ctx.stroke();
      if (b.outcome !== 'Averted') {
        const k = r * 0.5;
        ctx.beginPath(); ctx.moveTo(p[0] - k, p[1] - k); ctx.lineTo(p[0] + k, p[1] + k); ctx.moveTo(p[0] + k, p[1] - k); ctx.lineTo(p[0] - k, p[1] + k);
        ctx.stroke();
      }
    }
  }
}
