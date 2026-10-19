import { useApp, useViewModel } from './hooks';
import { SHELL_HINTS } from './glossary';
import styles from './StatusBar.module.css';

/** Engineering telemetry (per-stage timings, ms/frame) is opt-in: `?debug` in the URL. */
const debug = typeof location !== 'undefined' && new URLSearchParams(location.search).has('debug');

export function StatusBar() {
  const { vms } = useApp();
  const s = useViewModel(vms.shell);
  const c = s.cursor;
  const t = s.timings;
  const total = t.totalMs ? `Generated in ${(t.totalMs / 1000).toFixed(1)} s` : '';
  const breakdown = `gen ${Math.round(t.generateMs ?? 0)} + raster ${Math.round(t.rasterMs ?? 0)} + features ${Math.round(t.featuresMs ?? 0)} + names ${Math.round(t.namesMs ?? 0)} ms`;
  return (
    <footer className={styles.statusbar} data-testid="statusbar">
      {s.busy && <span className={styles.busy}><span className={styles.spinner} />{s.busy}<button className="mini" onClick={vms.shell.cancelWork} title="Stop and start a fresh engine: use it if a big world is taking too long">Cancel</button></span>}
      <span className="grow">{s.status}</span>
      {c && (
        <span title={SHELL_HINTS.cursor}>
          {fmtLat(c.lat)} {fmtLon(c.lon)} · h {c.elevation >= 0 ? '+' : ''}{c.elevation.toFixed(3)} · {c.biome}
          {c.feature && ` · ${c.feature}`}{c.climate && ` · ${c.climate}`} · {c.kmPerPx < 1 ? `${(c.kmPerPx * 1000).toFixed(0)} m/px` : `${c.kmPerPx.toFixed(1)} km/px`}
        </span>
      )}
      {total && <span className="muted" title={`${SHELL_HINTS.timings}\n${breakdown}`}>{total}</span>}
      {debug && <span className="muted">{breakdown} · {s.frame}</span>}
    </footer>
  );
}

const fmtLat = (v: number) => `${Math.abs(v).toFixed(2)}°${v >= 0 ? 'N' : 'S'}`;
const fmtLon = (v: number) => `${Math.abs(v).toFixed(2)}°${v >= 0 ? 'E' : 'W'}`;
