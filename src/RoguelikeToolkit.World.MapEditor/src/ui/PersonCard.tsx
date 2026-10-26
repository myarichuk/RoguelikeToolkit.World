import { useEffect, useRef } from 'react';
import type { PersonCard as Card } from '../viewmodels/PersonCardViewModel';
import { useApp, useViewModel } from './hooks';
import styles from './PersonCard.module.css';

const span = (from: number, to: number) => `t${from}–${to < 0 ? 'now' : `t${to}`}`;

/** One person of history: who they were, the offices they held, their bonds and battles. Floats beside the map; Esc closes it. */
export function PersonCard() {
  const { vms } = useApp();
  const vm = vms.person;
  const s = useViewModel(vm);
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => { if (s.open) root.current?.focus(); }, [s.open, s.card?.id]);
  if (!s.open || !s.card) return null;
  const c = s.card, prev = s.trail[s.trail.length - 1];
  return (
    <div ref={root} className={styles.card} role="dialog" aria-labelledby="person-title" tabIndex={-1} data-testid="person-card"
      onKeyDown={(e) => { if (e.key === 'Escape') vm.close(); }}>
      <header className={styles.head}>
        {prev && <button className="mini" onClick={vm.back} title={`Back to ${prev.name}`} aria-label="Back">←</button>}
        <h2 id="person-title">{c.name}</h2>
        <button className="mini" onClick={vm.close} aria-label="Close">✕</button>
      </header>
      <div className={styles.body}>
        <div className={styles.sub}>
          <span>{c.kind}</span>
          {c.realm ? <><i className={styles.dot} style={{ background: c.color }} /><span>{c.realm}</span></> : <span>outside the law</span>}
          <span>· {c.folk}{c.culture ? ` · ${c.culture}` : ''}</span>
        </div>
        {c.epithets.length > 0 && <div className={styles.epithets}>{c.epithets.map((e) => <span key={e} className={styles.epithet}>{e}</span>)}</div>}
        <Lifespan card={c} turns={s.turns} turn={s.turn} />
        <p className={styles.note}>
          Born turn {c.born}, {c.living ? `living (${c.age} years old at the end)` : `died turn ${c.died}${c.deathCause ? ` (${c.deathCause.toLowerCase()})` : ''}, aged ${c.age}`}
          {c.parent && <> · child of <button className={styles.link} onClick={() => vm.show(c.parent!.id)}>{c.parent.name}</button></>}
        </p>
        <dl className={styles.figures}>{c.figures.map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>
        {c.sponsor && (
          <section>
            <h3>Backed by</h3>
            <p className={styles.note} style={{ color: 'inherit' }}>
              {c.sponsor.realm || 'an unknown realm'}
              {c.sponsor.person && <> · run by <button className={styles.link} onClick={() => vm.show(c.sponsor!.person!.id)}>{c.sponsor.person.name}</button></>}
            </p>
          </section>
        )}
        {c.offices.length > 0 && (
          <section>
            <h3>Offices</h3>
            <ul className={styles.list}>{c.offices.map((o, i) => <li key={i} className={styles.line}><span className={styles.turn}>{span(o.from, o.to)}</span><span>{o.office} of {o.realm}</span></li>)}</ul>
          </section>
        )}
        {c.relations.length > 0 && (
          <section>
            <h3>Bonds</h3>
            <ul className={styles.list}>
              {c.relations.map((r, i) => (
                <li key={i} className={styles.line}>
                  <span className={styles.turn}>{span(r.from, r.to)}</span>
                  <span>{r.outgoing ? `${r.kind} of` : `${r.kind}:`} <button className={styles.link} data-testid="person-link" onClick={() => vm.show(r.other.id)}>{r.other.name}</button></span>
                </li>
              ))}
            </ul>
          </section>
        )}
        {c.battles.length > 0 && (
          <section>
            <h3>Battles commanded ({c.battles.length})</h3>
            <ul className={styles.list}>
              {c.battles.map((b) => (
                <li key={b.id}>
                  <button className={styles.row} onClick={() => { vm.showTurn(b.turn); vm.showOnMap(b.tile); }} title="Show this turn on the map">
                    <span className={styles.turn}>t{b.turn}</span>
                    <span><i className={styles.swatch} style={{ background: b.color }} />{b.kind}, {b.side} against {b.enemy || 'an enemy'}: {b.outcome.toLowerCase()}{b.won === true ? ' (won)' : b.won === false ? ' (lost)' : ''}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        )}
        {c.events.length > 0 && (
          <section>
            <h3>Story</h3>
            <ul className={styles.list}>
              {c.events.map((e) => (
                <li key={e.id}><button className={styles.row} disabled={e.tile < 0} onClick={() => vm.showOnMap(e.tile)}><span className={styles.turn}>t{e.turn}</span><span>{e.text}</span></button></li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </div>
  );
}

/** Born to died against the whole of history, offices shaded under it, the scrubbed turn as a line. */
function Lifespan({ card, turns, turn }: { card: Card; turns: number; turn: number }) {
  const w = Math.max(1, turns);
  return (
    <div>
      <svg className={styles.life} viewBox={`0 0 ${w} 10`} preserveAspectRatio="none" role="img" aria-label={`Lived from turn ${card.born} to ${card.living ? 'the end' : `turn ${card.died}`}`}>
        <rect className={styles.lifeBar} x={card.born} width={Math.max(0.3, card.died - card.born)} y={1} height={8} />
        {card.offices.map((o, i) => <rect key={i} className={styles.office} x={o.from} width={Math.max(0.3, (o.to < 0 ? card.died : o.to) - o.from)} y={6} height={3} />)}
        <line className={styles.now} x1={turn} x2={turn} y1={0} y2={10} />
      </svg>
      <div className={styles.axis}><span>t0</span><span>t{turns}</span></div>
    </div>
  );
}
