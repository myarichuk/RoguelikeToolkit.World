import { humanize } from '../engine/world/history/record';
import { EVENT_ROWS, PERSON_ROWS } from '../viewmodels/HistoryViewModel';
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
        {s.expansion && (
          <>
            <Toggle label="Show battles and sieges" on={s.battlesLayer} count={s.marks.battles + s.marks.sieges} onChange={() => vm.setLayer('battles', !s.battlesLayer)} hint="Marks the battles of this turn, coloured by who won, and rings the places under siege. Same as the Battles & sieges layer in the View tab." />
            <Toggle label="Show outlaws and raids" on={s.outlawsLayer} count={s.marks.havens + s.marks.raids} onChange={() => vm.setLayer('outlaws', !s.outlawsLayer)} hint="Marks where outlaw bands hid at this turn, and pins the caravans raided in it." />
            <Toggle label="Show road safety" on={s.routeSafetyLayer} onChange={() => vm.setLayer('routeSafety', !s.routeSafetyLayer)} hint="Colours the trade roads by how safe they were at this turn: red is deadly, green is safe." />
          </>
        )}
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
      {s.expansion && (
        <Section title={`People (${s.personCount})`} hint="The persons living at the chosen turn, the most renowned first (narrowed to the chosen realm). Click one to open their card.">
          {s.personCount > PERSON_ROWS && <p className={styles.when}>The {PERSON_ROWS} most renowned of {s.personCount}</p>}
          <ul className={styles.list} data-testid="history-persons">
            {s.persons.map((p) => (
              <li key={p.id}>
                <button className={styles.row} onClick={() => vms.person.show(p.id)}>
                  <i className={styles.dot} style={{ background: p.color }} />
                  <span><span className={styles.name}>{p.name}</span><small className={styles.meta}>{p.office || p.kind} · {p.realm}</small></span>
                  <span className={styles.turn} />
                </button>
              </li>
            ))}
          </ul>
        </Section>
      )}
      <Section title={s.expansion ? 'Chronicle' : 'Events'} hint={s.expansion
        ? 'What happened up to the chosen turn, newest first, as the chronicle tells it. Click an event to centre on where it happened, a name to open that person\'s card.'
        : 'What happened up to the chosen turn, newest first. Click an event to centre on where it happened.'}>
        <Toggle label="Only major events" on={s.majorOnly} onChange={() => vm.setMajorOnly(!s.majorOnly)} />
        {s.expansion && <Toggle label="Only this turn" on={s.turnOnly} onChange={() => vm.setTurnOnly(!s.turnOnly)} />}
        {s.polity !== null && <button className="wide" onClick={() => vm.setPolity(null)}>Showing one realm · show all</button>}
        <p className={styles.when}>{s.eventCount > EVENT_ROWS ? `Latest ${EVENT_ROWS} of ${s.eventCount}` : `${s.eventCount} event${s.eventCount === 1 ? '' : 's'}`}</p>
        <ul className={styles.list}>
          {s.events.map((e) => (
            <li key={e.id}>
              <button className={`${styles.event} ${e.major ? '' : styles.minor}`} disabled={e.tile < 0} onClick={() => vm.focusTile(e.tile)} title={e.battle?.label}>
                <span className={styles.turn}>t{e.turn}</span><span>{e.battle && <i className={styles.swatch} style={{ background: e.battle.color }} />}{e.text}</span>
              </button>
              {e.persons.length > 0 && (
                <div className={styles.people}>
                  {e.persons.map((p) => <button key={p.id} className={styles.person} data-testid="event-person" onClick={() => vms.person.show(p.id)}>{p.name}</button>)}
                </div>
              )}
            </li>
          ))}
        </ul>
      </Section>
    </>
  );
}
