// Vector geometry from the same signed distance field the GPU draws: coast = iso 0, ripple rings
// = iso -k*ring. Offsets of the coast are just other iso-levels of the SDF, so screen and export
// agree without any polygon offsetting.

export type Polyline = [number, number][] & { closed?: boolean; wrap?: boolean };
export type Lines = Polyline[] & { northInside?: boolean };

/**
 * Marching squares on a W x H grid that wraps in x. Rows outside the map (beyond the Mercator
 * cut) count as sea, so every contour closes. Returns polylines in grid coordinates (pixel
 * centers at i + 0.5), x unwrapped along each line so a contour crossing the antimeridian stays
 * continuous. Rings keep land on their right (positive area, y down).
 */
export function contour(field: Float32Array, W: number, H0: number, iso: number): Lines {
  const H = H0 + 2;
  const SEA = -1e3;
  const v = (x: number, y: number) => (y === 0 || y === H - 1 ? SEA : field[(y - 1) * W + (((x % W) + W) % W)] - iso);
  const hEdge = (x: number, y: number) => 2 * (y * W + (((x % W) + W) % W));
  const vEdge = (x: number, y: number) => 2 * (y * W + (((x % W) + W) % W)) + 1;
  const pointOn = (id: number): [number, number] => {
    const cell = id >> 1, x = cell % W, y = (cell / W) | 0;
    if ((id & 1) === 0) { const a = v(x, y), b = v(x + 1, y); return [x + a / (a - b) + 0.5, y - 0.5]; }
    const a = v(x, y), b = v(x, y + 1); return [x + 0.5, y + a / (a - b) - 0.5];
  };
  const next = new Map<number, number>();
  for (let y = 0; y < H - 1; y++) {
    for (let x = 0; x < W; x++) {
      const a = v(x, y) >= 0, b = v(x + 1, y) >= 0, c = v(x + 1, y + 1) >= 0, d = v(x, y + 1) >= 0;
      const code = (a ? 8 : 0) | (b ? 4 : 0) | (c ? 2 : 0) | (d ? 1 : 0);
      if (code === 0 || code === 15) continue;
      const T = hEdge(x, y), R = vEdge(x + 1, y), B = hEdge(x, y + 1), L = vEdge(x, y);
      const seg = (from: number, to: number) => next.set(from, to);
      switch (code) {
        case 1: seg(L, B); break; case 14: seg(B, L); break;
        case 2: seg(B, R); break; case 13: seg(R, B); break;
        case 3: seg(L, R); break; case 12: seg(R, L); break;
        case 4: seg(R, T); break; case 11: seg(T, R); break;
        case 6: seg(B, T); break; case 9: seg(T, B); break;
        case 7: seg(L, T); break; case 8: seg(T, L); break;
        case 5: case 10: {
          const center = (v(x, y) + v(x + 1, y) + v(x + 1, y + 1) + v(x, y + 1)) / 4 >= 0;
          if (code === 5) { if (center) { seg(L, T); seg(R, B); } else { seg(L, B); seg(R, T); } }
          else { if (center) { seg(T, R); seg(B, L); } else { seg(T, L); seg(B, R); } }
          break;
        }
      }
    }
  }
  const lines: Lines = [];
  while (next.size) {
    const start = next.keys().next().value as number;
    const pts: Polyline = [];
    let e: number | undefined = start, prevX: number | null = null;
    while (e !== undefined) {
      let [px, py] = pointOn(e);
      if (prevX !== null) { while (px - prevX > W / 2) px -= W; while (prevX - px > W / 2) px += W; }
      pts.push([px, py]); prevX = px;
      const n: number | undefined = next.get(e); next.delete(e); e = n;
      if (e === start) { pts.closed = true; break; }
    }
    if (Math.abs(pts[pts.length - 1][0] - pts[0][0]) > W / 2) { pts.wrap = true; pts.closed = false; }
    lines.push(pts);
  }
  lines.northInside = false;
  return lines;
}

export function chaikin(pts: Polyline, iterations = 2): Polyline {
  for (let it = 0; it < iterations; it++) {
    const out: Polyline = [], n = pts.length, last = pts.closed ? n : n - 1;
    if (!pts.closed) out.push(pts[0]);
    for (let i = 0; i < last; i++) {
      const [ax, ay] = pts[i], [bx, by] = pts[(i + 1) % n];
      out.push([0.75 * ax + 0.25 * bx, 0.75 * ay + 0.25 * by], [0.25 * ax + 0.75 * bx, 0.25 * ay + 0.75 * by]);
    }
    if (!pts.closed) out.push(pts[n - 1]);
    out.closed = pts.closed; out.wrap = pts.wrap; pts = out;
  }
  return pts;
}

export function simplify(pts: Polyline, tol: number): Polyline {
  if (pts.length < 4) return pts;
  const keep = new Uint8Array(pts.length); keep[0] = keep[pts.length - 1] = 1;
  const stack: [number, number][] = [[0, pts.length - 1]], t2 = tol * tol;
  while (stack.length) {
    const [i, j] = stack.pop()!;
    const [ax, ay] = pts[i], [bx, by] = pts[j];
    const dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy || 1e-12;
    let best = -1, bd = t2;
    for (let k = i + 1; k < j; k++) {
      const t = Math.max(0, Math.min(1, ((pts[k][0] - ax) * dx + (pts[k][1] - ay) * dy) / len2));
      const ex = ax + t * dx - pts[k][0], ey = ay + t * dy - pts[k][1], d = ex * ex + ey * ey;
      if (d > bd) { bd = d; best = k; }
    }
    if (best >= 0) { keep[best] = 1; stack.push([i, best], [best, j]); }
  }
  const out = pts.filter((_, k) => keep[k]) as Polyline; out.closed = pts.closed; out.wrap = pts.wrap; return out;
}

export function shape(lines: Lines, tolerance: number): Lines {
  const out = lines.map((l) => chaikin(simplify(l, tolerance), 2)) as Lines;
  out.northInside = lines.northInside;
  return out;
}

/**
 * Path data for a set of contour lines, scaled by `k` (grid -> map units). Each ring is emitted
 * at x - W, x, x + W where that copy reaches the frame, in one path, so holes stay holes across
 * the antimeridian under nonzero fill. Planet-circling lines close through the bottom edge.
 */
export function pathData(lines: Lines, minLen: number, W: number, H: number, fill: boolean, k = 1): string {
  const f = (v: number) => (v * k).toFixed(1);
  let d = '';
  for (const l of lines) {
    if (l.length < minLen) continue;
    let lo = Infinity, hi = -Infinity;
    for (const [x] of l) { if (x < lo) lo = x; if (x > hi) hi = x; }
    for (const off of [-W, 0, W]) {
      if (hi + off < 0 || lo + off > W) continue;
      d += 'M' + l.map(([x, y]) => `${f(x + off)} ${f(y)}`).join('L');
      if (l.wrap && fill) d += `L${f(l[l.length - 1][0] + off)} ${f(H + 2)}L${f(l[0][0] + off)} ${f(H + 2)}Z`;
      else if (l.closed) d += 'Z';
    }
  }
  return d;
}
