// Renders every stamp generator x 6 seeds into one labelled SVG contact sheet.
// Usage: npx tsx tools/contact-sheet.ts <out.svg> [variants=6] [id,id,...]
// Each cell fits the stamp; a small dot marks the anchor. Bytes per stamp are shown.

import { GENERATORS, DEFAULT_PALETTE, makeStamp, stampToSvg } from '../src/engine/procgen/stamps';

interface NodeLike { process: { argv: string[]; exit(code: number): never } }
const proc = (globalThis as unknown as NodeLike).process;

async function main(): Promise<void> {
  const out = proc.argv[2];
  if (!out) { console.error('usage: contact-sheet.ts <out.svg> [variants]'); proc.exit(1); }
  const variants = Number(proc.argv[3] ?? 6) || 6;
  const fs = (await import('node:fs' as string)) as { writeFileSync(p: string, s: string): void };

  const cell = 190, label = 30, gutter = 10;
  const only = proc.argv[4]?.split(',').filter((x) => x in GENERATORS);
  const ids = only && only.length ? only : Object.keys(GENERATORS);
  const W = 170 + variants * (cell + gutter);
  const H = ids.length * (cell + gutter) + 20;
  const pal = DEFAULT_PALETTE;
  let body = '';
  const t0 = performance.now();
  let count = 0, bytes = 0;
  ids.forEach((id, row) => {
    const g = GENERATORS[id]!;
    const y0 = 10 + row * (cell + gutter);
    body += `<text x="12" y="${y0 + cell / 2}" font-size="16" font-family="Georgia,serif" fill="${pal.ink}">${g.label}</text>`;
    body += `<text x="12" y="${y0 + cell / 2 + 18}" font-size="11" font-family="monospace" fill="${pal.ink}" opacity=".7">${id} / ${g.defaultSize}</text>`;
    for (let v = 0; v < variants; v++) {
      const seed = 1000 + v * 7919;
      const s = makeStamp(id, seed);
      count++;
      const inner = stampToSvg(s);
      const json = JSON.stringify(s).length;
      bytes += json;
      const x0 = 170 + v * (cell + gutter);
      const avail = cell - label;
      const k = Math.min((cell - 16) / s.w, (avail - 8) / s.h);
      const ox = x0 + (cell - s.w * k) / 2, oy = y0 + 4 + (avail - s.h * k) / 2;
      body += `<rect x="${x0}" y="${y0}" width="${cell}" height="${cell}" fill="none" stroke="${pal.ink}" stroke-opacity=".12"/>`;
      body += `<g transform="translate(${ox.toFixed(1)} ${oy.toFixed(1)}) scale(${k.toFixed(4)})">${inner}`;
      body += `<circle cx="${s.anchor[0]}" cy="${s.anchor[1]}" r="${(1.6 / k).toFixed(2)}" fill="#c0392b" opacity=".6"/></g>`;
      body += `<text x="${x0 + 6}" y="${y0 + cell - 8}" font-size="10" font-family="monospace" fill="${pal.ink}" opacity=".6">seed ${seed} · ${json}B json · ${s.shapes.length} shapes</text>`;
    }
  });
  const ms = performance.now() - t0;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}"><rect width="100%" height="100%" fill="${pal.paper}"/>${body}</svg>`;
  fs.writeFileSync(out, svg);
  console.log(`wrote ${out}: ${count} stamps, ${(bytes / count).toFixed(0)} B/stamp avg (JSON), ${ms.toFixed(1)} ms (incl. svg)`);
}

void main();
