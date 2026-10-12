import { useApp, useViewModel } from './hooks';

export function StatusBar() {
  const { vms } = useApp();
  const s = useViewModel(vms.shell);
  const c = s.cursor;
  const t = s.timings;
  const gen = t.totalMs ? `gen ${Math.round(t.generateMs ?? 0)} + raster ${Math.round(t.rasterMs ?? 0)} + features ${Math.round(t.featuresMs ?? 0)} + names ${Math.round(t.namesMs ?? 0)} ms` : '';
  return (
    <footer className="statusbar">
      <span className="grow">{s.status}</span>
      {c && (
        <span>
          {fmtLat(c.lat)} {fmtLon(c.lon)} · h {c.elevation >= 0 ? '+' : ''}{c.elevation.toFixed(3)} · {c.biome}
          {c.feature && ` · ${c.feature}`}{c.climate && ` · ${c.climate}`} · {c.kmPerPx < 1 ? `${(c.kmPerPx * 1000).toFixed(0)} m/px` : `${c.kmPerPx.toFixed(1)} km/px`}
        </span>
      )}
      {gen && <span className="muted" title={JSON.stringify(t, null, 1)}>{gen}</span>}
      <span className="muted">{s.frame}</span>
    </footer>
  );
}

const fmtLat = (v: number) => `${Math.abs(v).toFixed(2)}°${v >= 0 ? 'N' : 'S'}`;
const fmtLon = (v: number) => `${Math.abs(v).toFixed(2)}°${v >= 0 ? 'E' : 'W'}`;
