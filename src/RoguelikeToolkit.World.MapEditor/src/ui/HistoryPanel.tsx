import { humanize } from '../engine/world/history/record';
import { EVENT_ROWS } from '../viewmodels/HistoryViewModel';
import { useApp, useViewModel } from './hooks';
import { Section, Toggle } from './controls';
import styles from './HistoryPanel.module.css';

const FORM_LABEL: Record<string, string> = { Kingdom: 'Kingdom', MerchantRepublic: 'Merchant republic', LooseAssociation: 'Loose association', Horde: 'Horde' };

export function HistoryPanel() {
  const { vms } = useApp();
  const vm = vms.history;
  const s = useViewModel(vm);
  if (!s.has) {
    return (
      <Section title="History">
        <p className={`hint ${styles.empty}`}>This map has no recorded history. Generate a world with “Simulate history” on (New world wizard, Peoples step) to see its realms, wars and timeline here.</p>
      </Section>
    );
  }
  const alive = s.realms.filter((r) => r.alive).length;
  return (
    <>
      <Section title="Timeline" hint="Drag to replay history. The territory tint, the realm list and the event log all show the world as it stood at that turn.">
        <div className={styles.scrub}>
          <input type="range" min={0} max={s.turns} step={1} value={s.turn} aria-label="History turn" onChange={(e) => vm.setTurn(Number(e.target.value))} />
        </div>
        <div className={styles.when}>Turn {s.turn} of {s.turns} · year {s.turn * s.turnYears} · {alive} realm{alive === 1 ? '' : 's'} standing</div>
        <button className="wide" onClick={vms.historyWindow.show} title="Every realm on a timeline, the titles they earned, and each one's story">Open history window…</button>
        <Toggle label="Show territory on the map" on={s.realmsLayer} onChange={() => vm.setLayer('realms', !s.realmsLayer)} hint="Tints the land by the realm that held it. Same as the Realms layer in the View tab." />
        <Toggle label="Show realm borders and names" on={s.bordersLayer} onChange={() => vm.setLayer('borders', !s.bordersLayer)} hint="Outlines each realm and writes its name. Same as the Realm borders layer in the View tab." />
      </Section>
      <Section title={`Realms (${s.realms.length})`} hint="Click a realm to centre on its seat and narrow the event log to it. Faded realms have fallen by this turn.">
        <ul className={styles.list}>
          {s.realms.map((r) => (
            <li key={r.id}>
              <button className={`${styles.row} ${r.alive ? '' : styles.dead}`} aria-pressed={s.polity === r.id} onClick={() => vm.selectRealm(r.id)}>
                <i className={styles.dot} style={{ background: r.color }} />
                <span>
                  <span className={styles.name}>{r.name}</span>
                  <small className={styles.meta}>{FORM_LABEL[r.form] ?? r.form} · {humanize(r.folk)}{r.culture ? ` · ${humanize(r.culture)}` : ''}{r.parent ? ` · broke from ${r.parent}` : ''}{!r.alive ? ` · fell turn ${r.ended}${r.endCause ? ` (${humanize(r.endCause)})` : ''}` : ''}</small>
                </span>
                <span className={styles.turn}>{r.alive ? `${r.tiles.toLocaleString()} tiles` : `t${r.founded}–${r.ended}`}</span>
              </button>
            </li>
          ))}
        </ul>
      </Section>
      <Section title="Events" hint="What happened up to the chosen turn, newest first. Click an event to centre on where it happened.">
        <Toggle label="Only major events" on={s.majorOnly} onChange={() => vm.setMajorOnly(!s.majorOnly)} />
        {s.polity !== null && <button className="wide" onClick={() => vm.setPolity(null)}>Showing one realm · show all</button>}
        <p className={styles.when}>{s.eventCount > EVENT_ROWS ? `Latest ${EVENT_ROWS} of ${s.eventCount}` : `${s.eventCount} event${s.eventCount === 1 ? '' : 's'}`}</p>
        <ul className={styles.list}>
          {s.events.map((e) => (
            <li key={e.id}>
              <button className={`${styles.event} ${e.major ? '' : styles.minor}`} disabled={e.tile < 0} onClick={() => vm.focusTile(e.tile)}>
                <span className={styles.turn}>t{e.turn}</span><span>{e.text}</span>
              </button>
            </li>
          ))}
        </ul>
      </Section>
    </>
  );
}
