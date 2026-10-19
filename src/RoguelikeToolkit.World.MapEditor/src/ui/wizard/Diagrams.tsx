import { useMemo } from 'react';
import type { PlanetPreset } from '../../engine/world/presets';

/** A small globe tinted by a preset: cold at the poles, hot at the equator, with more blue the wetter the world. */
export function PlanetGlyph({ preset, size = 56 }: { preset: PlanetPreset; size?: number }) {
  const id = `g-${preset.id}`;
  const [cold, mild, hot] = preset.look.tint;
  const tilt = preset.planet.obliquity;
  return (
    <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor={cold} /><stop offset="0.28" stopColor={mild} /><stop offset="0.5" stopColor={hot} />
          <stop offset="0.72" stopColor={mild} /><stop offset="1" stopColor={cold} />
        </linearGradient>
        <clipPath id={`${id}-c`}><circle cx="32" cy="32" r="22" /></clipPath>
      </defs>
      <g transform={`rotate(${Math.min(tilt, 60) * 0.6} 32 32)`}>
        <circle cx="32" cy="32" r="22" fill={`url(#${id})`} />
        <g clipPath={`url(#${id}-c)`} opacity={0.35 + preset.look.wet * 0.4}>
          <ellipse cx="22" cy="26" rx="9" ry="5" fill="#4f86b0" /><ellipse cx="40" cy="40" rx="10" ry="6" fill="#4f86b0" />
        </g>
        <line x1="32" y1="4" x2="32" y2="60" stroke="currentColor" strokeWidth="1" strokeDasharray="2 2" opacity="0.6" />
      </g>
    </svg>
  );
}

/**
 * The planet, its spin axis and the light it receives. The axis leans by the tilt; the star on the left grows with the flux;
 * the arrow's length is the spin. Nothing here is measured: it only shows what each slider turns.
 */
export function TiltDiagram({ tilt, flux, spin }: { tilt: number; flux: number; spin: number }) {
  const rays = Math.max(3, Math.round(3 + flux * 4));
  return (
    <svg viewBox="0 0 260 150" role="img" aria-label={`Planet tilted ${tilt.toFixed(0)} degrees, ${flux.toFixed(2)} times Earth's starlight, spinning ${spin.toFixed(2)} times Earth's rate`}>
      <circle cx="34" cy="75" r={10 + flux * 6} fill="#e8b84a" />
      {Array.from({ length: rays }, (_, i) => {
        const a = ((i / rays) * 2 - 1) * 0.9;
        return <line key={i} x1="58" y1={75 + Math.sin(a) * 30} x2="118" y2={75 + Math.sin(a) * 38} stroke="#e8b84a" strokeWidth="1.4" opacity="0.55" />;
      })}
      <g transform={`translate(175 75) rotate(${tilt})`}>
        <circle r="44" fill="#3d6f93" />
        <ellipse rx="44" ry="11" fill="none" stroke="#e8e0d0" strokeWidth="1" opacity="0.7" />
        <path d="M-30 -20 q14 -10 30 -4 t26 10 a44 44 0 0 0 -56 -6z" fill="#6c9a5a" opacity="0.9" />
        <path d="M-16 18 q12 8 24 2 t14 -8 a44 44 0 0 1 -38 6z" fill="#c9a45a" opacity="0.85" />
        <line y1="-62" y2="62" stroke="#e8e0d0" strokeWidth="1.4" />
        <circle cy="-62" r="2.6" fill="#e8e0d0" />
        <path d={`M-22 ${-52} a 24 24 0 0 1 ${Math.min(44, 14 + spin * 12)} 0`} fill="none" stroke="#c8a46a" strokeWidth="1.6" markerEnd="url(#arrow)" />
      </g>
      <defs><marker id="arrow" viewBox="0 0 6 6" refX="5" refY="3" markerWidth="5" markerHeight="5" orient="auto"><path d="M0 0 L6 3 L0 6z" fill="#c8a46a" /></marker></defs>
      <text x="175" y="146" textAnchor="middle" fontSize="10" fill="currentColor" opacity="0.7">axis tilt {tilt.toFixed(0)}°</text>
    </svg>
  );
}

const PALETTE = ['#7b9e6b', '#c9a45a', '#6c8fb0', '#b57b6a', '#8f7ab0', '#6fa89a', '#b0a06a', '#9a8070'];

/** n plates as a nearest-seed mosaic (seeds are deterministic, so the picture does not shimmer). */
export function PlatesDiagram({ plates }: { plates: number }) {
  const cells = useMemo(() => {
    const W = 26, H = 13, seeds: [number, number][] = [];
    let s = 1234567;
    const rnd = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
    for (let i = 0; i < plates; i++) seeds.push([rnd() * W, rnd() * H]);
    const out: { x: number; y: number; k: number }[] = [];
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      let best = 0, bd = Infinity;
      seeds.forEach(([sx, sy], i) => {
        const dx = Math.min(Math.abs(x - sx), W - Math.abs(x - sx)), d = dx * dx + (y - sy) ** 2;   // wraps east-west like the map
        if (d < bd) { bd = d; best = i; }
      });
      out.push({ x, y, k: best });
    }
    return out;
  }, [plates]);
  return (
    <svg viewBox="0 0 260 130" role="img" aria-label={`${plates} tectonic plates`}>
      {cells.map((c) => <rect key={`${c.x}-${c.y}`} x={c.x * 10} y={c.y * 10} width="10.5" height="10.5" fill={PALETTE[c.k % PALETTE.length]} opacity={0.55 + (c.k % 3) * 0.12} />)}
    </svg>
  );
}

/** Equirectangular click-to-place map for a realm's capital: the world does not exist yet, so it is a plain grid. */
export function PlaceMap({ lat, lon, onPick }: { lat: number | null; lon: number | null; onPick: (lat: number, lon: number) => void }) {
  const W = 240, H = 120;
  const pick = (e: React.MouseEvent<SVGSVGElement>) => {
    const r = e.currentTarget.getBoundingClientRect();
    const x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
    onPick(Math.round(90 - y * 180), Math.round(x * 360 - 180));
  };
  return (
    <svg viewBox={`0 0 ${W} ${H}`} onClick={pick} role="img" aria-label="Click to choose the capital's latitude and longitude" style={{ cursor: 'crosshair', width: '100%', display: 'block', background: 'color-mix(in srgb, var(--bg) 70%, #3d6f93)', borderRadius: 4 }}>
      {[-60, -30, 0, 30, 60].map((l) => <line key={l} x1="0" x2={W} y1={H / 2 - (l / 180) * H} y2={H / 2 - (l / 180) * H} stroke="currentColor" opacity={l === 0 ? 0.4 : 0.15} />)}
      {[-120, -60, 0, 60, 120].map((l) => <line key={l} y1="0" y2={H} x1={W / 2 + (l / 360) * W} x2={W / 2 + (l / 360) * W} stroke="currentColor" opacity={l === 0 ? 0.4 : 0.15} />)}
      {lat !== null && lon !== null && <circle cx={W / 2 + (lon / 360) * W} cy={H / 2 - (lat / 180) * H} r="4" fill="var(--accent)" stroke="var(--bg)" />}
    </svg>
  );
}
