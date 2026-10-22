import { Fragment, useEffect, useRef } from 'react';
import { LOG_ROWS, type ActorDetail, type ActorRow } from '../viewmodels/HistoryWindowViewModel';
import type { Scope } from '../engine/world/history/actors';
import { Segmented, Toggle } from './controls';
import { useApp, useViewModel } from './hooks';
import styles from './HistoryWindow.module.css';

const SCOPES: { value: Scope; label: string; hint: string }[] = [
  { value: 'realm', label: 'Realms', hint: 'Every realm on its own.' },
  { value: 'culture', label: 'Peoples', hint: 'Each folk and culture, with all its realms together.' },
];

/** The history window: who stood when, the titles they earned, and the story of the one picked. Esc closes it. */
export function HistoryWindow() {
  const { vms } = useApp();
  const vm = vms.historyWindow;
  const s = useViewModel(vm);
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => { if (s.open) root.current?.focus(); }, [s.open]);
  if (!s.open) return null;
  return (
    <div className={styles.backdrop} onMouseDown={(e) => { if (e.target === e.currentTarget) vm.close(); }}>
      <div ref={root} className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="history-title" tabIndex={-1}
        onKeyDown={(e) => { if (e.key === 'Escape') vm.close(); }}>
        <header className={styles.head}>
          <h2 id="history-title">History</h2>
          <Segmented<Scope> value={s.scope} options={SCOPES} onChange={vm.setScope} />
          <button className="mini" onClick={vm.close} aria-label="Close">✕</button>
        </header>
        <div className={styles.body}>
          {!s.has ? <p className={styles.note}>This map has no recorded history.</p> : (
            <>
              <section>
                <h3>Timeline</h3>
                <Chart rows={s.actors} turns={s.turns} selected={s.selected} detail={s.detail} onSelect={vm.select} />
              </section>
              {s.detail && <Detail detail={s.detail} majorOnly={s.majorOnly} onMajorOnly={vm.setMajorOnly} onShow={vm.showOnMap} />}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

/** One bar per stretch an actor stood, one row per actor; the picked actor's major events are ticked on its row. */
function Chart({ rows, turns, selected, detail, onSelect }: { rows: ActorRow[]; turns: number; selected: string | null; detail: ActorDetail | null; onSelect: (id: string) => void }) {
  const step = Math.max(1, Math.ceil(turns / 8 / 5) * 5);
  const marks = Array.from({ length: Math.floor(turns / step) + 1 }, (_, i) => i * step);
  return (
    <div className={styles.chart}>
      <div className={styles.axis}><span /><span>{marks.map((t) => <span key={t}>t{t}</span>)}</span></div>
      {rows.map((r) => (
        <button key={r.id} className={`${styles.row} ${r.standing ? '' : styles.fallen}`} aria-pressed={selected === r.id} onClick={() => onSelect(r.id)}>
          <span className={styles.who}>
            <i className={styles.dot} style={{ background: r.color }} /><span>{r.name}</span>
            {r.titles > 0 && <span className={styles.badge} title={`${r.titles} title${r.titles === 1 ? '' : 's'}`}>★{r.titles}</span>}
          </span>
          <svg className={styles.bars} viewBox={`0 0 ${Math.max(1, turns)} 10`} preserveAspectRatio="none" role="img" aria-label={`${r.name}: ${r.standing ? 'still standing' : 'fell'}`}>
            {r.spans.map((sp, i) => <rect key={i} x={sp.from} width={Math.max(0.4, sp.until - sp.from)} y={1} height={8} fill={r.color} />)}
            {selected === r.id && detail && (
              <g className={styles.ticks}>
                {detail.events.filter((e) => e.major).map((e) => <line key={e.id} x1={e.turn + 0.5} x2={e.turn + 0.5} y1={0} y2={10} />)}
                {detail.achievements.filter((a) => a.turn !== undefined).map((a) => <line key={a.rule} className={styles.titleTick} x1={a.turn! + 0.5} x2={a.turn! + 0.5} y1={0} y2={10} />)}
              </g>
            )}
          </svg>
        </button>
      ))}
    </div>
  );
}

function Detail({ detail, majorOnly, onMajorOnly, onShow }: { detail: ActorDetail; majorOnly: boolean; onMajorOnly: (on: boolean) => void; onShow: (tile: number) => void }) {
  return (
    <div className={styles.cols}>
      <section>
        <h3><i className={styles.dot} style={{ background: detail.color, display: 'inline-block', marginRight: 'var(--s-2)' }} />{detail.name}</h3>
        <dl className={styles.figures}>{detail.figures.map(([k, v]) => <Fragment key={k}><dt>{k}</dt><dd>{v}</dd></Fragment>)}</dl>
        <h3>Titles</h3>
        {detail.achievements.length === 0
          ? <p className={styles.note}>No titles: nothing set it apart, or there is nobody to compare it with.</p>
          : <div className={styles.titles}>{detail.achievements.map((a) => <div key={a.rule} className={styles.title}><b>★ {a.title}</b><small>{a.text}</small></div>)}</div>}
      </section>
      <section>
        <h3>Events</h3>
        <Toggle label="Only major events" on={majorOnly} onChange={() => onMajorOnly(!majorOnly)} />
        <p className={styles.note}>{detail.eventCount > LOG_ROWS ? `First ${LOG_ROWS} of ${detail.eventCount} events` : `${detail.eventCount} event${detail.eventCount === 1 ? '' : 's'}`} · click one to see where it happened</p>
        <ul className={styles.log}>
          {detail.events.map((e) => (
            <li key={e.id}>
              <button className={`${styles.event} ${e.major ? '' : styles.minor}`} disabled={e.tile < 0} onClick={() => onShow(e.tile)}>
                <span className={styles.turn}>t{e.turn}</span><span>{e.text}</span>
              </button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
