// Vector export from the same signed distance the GPU draws. Coast = iso 0, ripple rings =
// iso -k*ring. That is the vector equivalent of the shader's ripple bands: offsets of the coast,
// obtained by contouring the distance field rather than by polygon offsetting.

/**
 * Marching squares on a W x H grid that wraps in x. Returns closed/open polylines in grid
 * coordinates (pixel centers at i + 0.5), x unwrapped along each line so a contour that
 * crosses the antimeridian stays continuous (and may extend past [0, W)).
 */
export function contour(field, W, H0, iso) {
  // Pad with one constant row per pole (the pole is a point: its row has one value). With x
  // wrapping too, every contour closes; nothing dangles off the top or bottom edge.
  const H = H0 + 2;
  const poleN = mean(field, 0, W), poleS = mean(field, (H0 - 1) * W, W);
  const v = (x, y) => (y === 0 ? poleN : y === H - 1 ? poleS : field[(y - 1) * W + ((x % W) + W) % W]) - iso;
  // Edge ids: horizontal edge (x,y)-(x+1,y) -> 2*(y*W+x); vertical (x,y)-(x,y+1) -> 2*(y*W+x)+1.
  const hEdge = (x, y) => 2 * (y * W + ((x % W) + W) % W);
  const vEdge = (x, y) => 2 * (y * W + ((x % W) + W) % W) + 1;
  const pointOn = (id) => {
    const cell = id >> 1, x = cell % W, y = (cell / W) | 0;
    if ((id & 1) === 0) { const a = v(x, y), b = v(x + 1, y); return [x + a / (a - b) + 0.5, y - 0.5]; }
    const a = v(x, y), b = v(x, y + 1); return [x + 0.5, y + a / (a - b) - 0.5];
  };
  const next = new Map(); // directed segment: entry edge -> exit edge (land kept on the left)
  for (let y = 0; y < H - 1; y++) {
    for (let x = 0; x < W; x++) {
      const a = v(x, y) >= 0, b = v(x + 1, y) >= 0, c = v(x + 1, y + 1) >= 0, d = v(x, y + 1) >= 0;
      const code = (a ? 8 : 0) | (b ? 4 : 0) | (c ? 2 : 0) | (d ? 1 : 0);
      if (code === 0 || code === 15) continue;
      const T = hEdge(x, y), R = vEdge(x + 1, y), B = hEdge(x, y + 1), L = vEdge(x, y);
      const seg = (from, to) => next.set(from, to);
      switch (code) {
        case 1: seg(L, B); break;            case 14: seg(B, L); break;
        case 2: seg(B, R); break;            case 13: seg(R, B); break;
        case 3: seg(L, R); break;            case 12: seg(R, L); break;
        case 4: seg(R, T); break;            case 11: seg(T, R); break;
        case 6: seg(B, T); break;            case 9: seg(T, B); break;
        case 7: seg(L, T); break;            case 8: seg(T, L); break;
        case 5: case 10: {
          const center = (v(x, y) + v(x + 1, y) + v(x + 1, y + 1) + v(x, y + 1)) / 4 >= 0;
          if (code === 5) { if (center) { seg(L, T); seg(R, B); } else { seg(L, B); seg(R, T); } }
          else { if (center) { seg(T, R); seg(B, L); } else { seg(T, L); seg(B, R); } }
          break;
        }
      }
    }
  }
  const lines = [];
  const take = (start) => {
    const pts = []; let e = start, prevX = null;
    while (e !== undefined) {
      let [px, py] = pointOn(e);
      if (prevX !== null) { while (px - prevX > W / 2) px -= W; while (prevX - px > W / 2) px += W; }
      pts.push([px, py]); prevX = px;
      const n = next.get(e); next.delete(e); e = n;
      if (e === start) { pts.closed = true; break; }
    }
    return pts;
  };
  while (next.size) {
    const ring = take(next.keys().next().value);
    // A ring that circles the planet unwraps to a curve from x to x +- W: it is not closed in
    // the plane. Flag it; filling closes it through the south pole (see pathData).
    if (Math.abs(ring[ring.length - 1][0] - ring[0][0]) > W / 2) { ring.wrap = true; ring.closed = false; }
    lines.push(ring);
  }
  // Whatever lies outside every ring has the north pole's sign.
  lines.northInside = poleN >= 0;
  return lines;
}

function mean(a, from, n) { let s = 0; for (let i = 0; i < n; i++) s += a[from + i]; return s / n; }

/** Chaikin corner cutting; closed rings stay closed. */
export function chaikin(pts, iterations = 2) {
  for (let it = 0; it < iterations; it++) {
    const out = [], n = pts.length, last = pts.closed ? n : n - 1;
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

/** Douglas-Peucker, iterative. */
export function simplify(pts, tol) {
  if (pts.length < 4) return pts;
  const keep = new Uint8Array(pts.length); keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]], t2 = tol * tol;
  while (stack.length) {
    const [i, j] = stack.pop();
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
  const out = pts.filter((_, k) => keep[k]); out.closed = pts.closed; out.wrap = pts.wrap; return out;
}

// Every ring is emitted at x - W, x, x + W (where that copy reaches the frame) in ONE path: a lake whose unwrapped x landed in a
// different period than its continent still cuts a hole under nonzero fill. The frame clips.
//
// Fill winding: rings keep the inside on their right (positive area, y down), holes come out
// negative. Planet-circling rings are closed down through the south pole, so each adds +-1 to
// everything south of it; a frame rectangle adds +1 when the north pole itself is inside. The
// winding number of any point is then exactly 1 inside and 0 outside, whatever the topology
// (a world whose land joins across both poles has oceans as holes with no outer ring at all).
const pathData = (lines, minLen, W, H, fill) => {
  let d = fill && lines.northInside ? `M0 -2L${W} -2L${W} ${H + 2}L0 ${H + 2}Z` : '';
  for (const l of lines) {
    if (l.length < minLen) continue;
    let lo = Infinity, hi = -Infinity;
    for (const [x] of l) { if (x < lo) lo = x; if (x > hi) hi = x; }
    for (const off of [-W, 0, W]) {
      if (hi + off < 0 || lo + off > W) continue; // copy lies wholly outside the frame
      d += 'M' + l.map(([x, y]) => `${(x + off).toFixed(1)} ${y.toFixed(1)}`).join('L');
      if (l.wrap && fill) d += `L${(l[l.length - 1][0] + off).toFixed(1)} ${H + 2}L${(l[0][0] + off).toFixed(1)} ${H + 2}Z`;
      else if (l.closed) d += 'Z';
    }
  }
  return d;
};

const shape = (lines, tolerance) => {
  const out = lines.map((l) => chaikin(simplify(l, tolerance), 2));
  out.northInside = lines.northInside;
  return out;
};

export function buildSvg(sdf, W, H, { ring = 6, rings = 4, tolerance = 0.35 } = {}) {
  const t0 = performance.now();
  const coast = shape(contour(sdf, W, H, 0), tolerance);
  const ringPaths = [];
  for (let k = 1; k <= rings; k++) ringPaths.push(pathData(shape(contour(sdf, W, H, -k * ring), tolerance), 6, W, H, false));
  const landD = pathData(coast, 4, W, H, true), coastD = pathData(coast, 4, W, H, false);
  const svg = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
<defs><clipPath id="frame"><rect width="${W}" height="${H}"/></clipPath></defs>
<g clip-path="url(#frame)">
  <g inkscape:groupmode="layer" inkscape:label="Sea"><rect width="${W}" height="${H}" fill="#bccfca"/></g>
  <g inkscape:groupmode="layer" inkscape:label="Ripples" fill="none" stroke="#2e4549" stroke-linejoin="round" stroke-linecap="round">
${ringPaths.map((d, i) => `    <path stroke-width="${(1.1 - i * 0.18).toFixed(2)}" opacity="${(0.7 - i * 0.13).toFixed(2)}"${i >= 2 ? ` stroke-dasharray="${18 - i * 4} ${3 + i * 3}"` : ''} d="${d}"/>`).join('\n')}
  </g>
  <g inkscape:groupmode="layer" inkscape:label="Land"><path fill="#e2d4ac" fill-rule="nonzero" d="${landD}"/></g>
  <g inkscape:groupmode="layer" inkscape:label="Coastline"><path fill="none" stroke="#2b2119" stroke-width="2" stroke-linejoin="round" d="${coastD}"/></g>
</g>
</svg>`;
  return { svg, ms: performance.now() - t0, coastLines: coast.length, bytes: svg.length };
}
