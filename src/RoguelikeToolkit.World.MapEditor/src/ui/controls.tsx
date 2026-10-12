import { useEffect, useId, useState, type ReactNode } from 'react';
import styles from './controls.module.css';

// Small, dumb, reusable controls. No state of their own beyond text-entry drafts.

// Remembered per title so a collapsed section stays collapsed when its tab is unmounted and remounted.
const sectionOpen = new Map<string, boolean>();

/**
 * A titled block. `collapsible` turns the title into a disclosure button; `summary` is what the
 * closed section shows in its place ("Earth", "8 selected") so the state is visible without opening it.
 */
export function Section({ title, children, actions, hint, collapsible, defaultOpen = true, summary }: {
  title: string; children: ReactNode; actions?: ReactNode; hint?: string; collapsible?: boolean; defaultOpen?: boolean; summary?: string;
}) {
  const [open, setOpen] = useState(() => !collapsible || (sectionOpen.get(title) ?? defaultOpen));
  const toggle = () => { sectionOpen.set(title, !open); setOpen(!open); };
  const [info, setInfo] = useState(false);
  return (
    <section className={styles.section}>
      <header>
        {collapsible
          ? <h3><button className="disclosure" aria-expanded={open} onClick={toggle}><span aria-hidden="true">{open ? '▾' : '▸'}</span>{title}</button></h3>
          : <h3>{title}</h3>}
        {hint && <InfoButton open={info} onToggle={() => setInfo(!info)} label={title} />}
        {collapsible && !open && summary && <span className={styles.summary}>{summary}</span>}
        {actions && open && <div className={styles.actions}>{actions}</div>}
      </header>
      {hint && info && <p className={`hint ${styles.hintInfo}`}>{hint}</p>}
      {open && children}
    </section>
  );
}


/**
 * A labelled row. The hint is a real (i) button, reachable by keyboard and touch, that expands the
 * explanation under the row; a `title` tooltip is neither. The label is wired with htmlFor, not
 * wrapped, because a button inside a <label> would swallow clicks meant for the control.
 */
function Field({ label, hint, variant, children }: { label: string; hint?: string; variant?: 'slider' | 'color'; children: (id: string) => ReactNode}) {
  const id = useId();
  const [open, setOpen] = useState(false);
  return (
    <div className={styles.field}>
      <div className={`${styles.row} ${variant ? styles[variant] : ''}`}>
        <span className={styles.lbl}>
          <label htmlFor={id}>{label}</label>
          {hint && <InfoButton open={open} onToggle={() => setOpen(!open)} label={label} />}
        </span>
        {children(id)}
      </div>
      {hint && open && <p className={`hint ${styles.hintInfo}`}>{hint}</p>}
    </div>
  );
}

export function InfoButton({ open, onToggle, label }: { open: boolean; onToggle: () => void; label: string }) {
  return (
    <button type="button" className={styles.info} aria-expanded={open} aria-label={`About ${label}`} onClick={onToggle}
      onKeyDown={(e) => { if (e.key === 'Escape' && open) { e.stopPropagation(); onToggle(); } }}>i</button>
  );
}

export function Slider({ label, value, min, max, step, onChange, format, hint }: {
  label: string; value: number; min: number; max: number; step: number; onChange: (v: number) => void; format?: (v: number) => string;
  /** Tooltip: one or two sentences saying what the term means. */
  hint?: string;
}) {
  return (
    <Field label={label} hint={hint} variant="slider">
      {(id) => (
        <>
          <output htmlFor={id}>{format ? format(value) : trim(value)}</output>
          <input id={id} type="range" min={min} max={max} step={step} value={value} onChange={(e) => onChange(parseFloat(e.target.value))} />
        </>
      )}
    </Field>
  );
}

export function NumberField({ label, value, onChange, min, max, step = 1, hint }: { label: string; value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number; hint?: string }) {
  return (
    <Field label={label} hint={hint}>
      {(id) => <input id={id} type="number" value={value} min={min} max={max} step={step} onChange={(e) => { const v = parseFloat(e.target.value); if (Number.isFinite(v)) onChange(v); }} />}
    </Field>
  );
}

/** Commits on Enter/blur so every keystroke is not an undo step. */
export function TextField({ label, value, onCommit, placeholder, hint }: { label?: string; value: string; onCommit: (v: string) => void; placeholder?: string; hint?: string }) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  const commit = () => { if (draft !== value) onCommit(draft); };
  const input = (id?: string) => (
    <input id={id} type="text" value={draft} placeholder={placeholder} onChange={(e) => setDraft(e.target.value)} onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') commit(); if (e.key === 'Escape') setDraft(value); }} />
  );
  return label ? <Field label={label} hint={hint}>{(id) => input(id)}</Field> : input();
}

export function Toggle({ label, on, onChange, swatch, count, hint }: { label: string; on: boolean; onChange: () => void; swatch?: ReactNode; count?: number; hint?: string }) {
  return (
    <label className={`${styles.toggle} ${on ? styles.toggleOn : ''}`} title={hint}>
      <input type="checkbox" checked={on} onChange={onChange} />
      {swatch}
      <span className="grow">{label}</span>
      {count !== undefined && <span className={styles.count}>{count}</span>}
    </label>
  );
}

export function ColorField({ label, value, onChange, hint }: { label: string; value: string; onChange: (v: string) => void; hint?: string }) {
  return (
    <Field label={label} hint={hint} variant="color">
      {(id) => (
        <>
          <input id={id} type="color" value={toHex(value)} onChange={(e) => onChange(e.target.value)} />
          <code>{value}</code>
        </>
      )}
    </Field>
  );
}

export function Select<T extends string>({ label, value, options, onChange, hint }: { label?: string; value: T; options: { value: T; label: string }[]; onChange: (v: T) => void; hint?: string }) {
  const sel = (id?: string) => (
    <select id={id} value={value} onChange={(e) => onChange(e.target.value as T)}>
      {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
    </select>
  );
  return label ? <Field label={label} hint={hint}>{(id) => sel(id)}</Field> : sel();
}

export function Segmented<T extends string>({ value, options, onChange }: { value: T; options: { value: T; label: string; hint?: string }[]; onChange: (v: T) => void }) {
  return (
    <div className={styles.segmented}>
      {options.map((o) => <button key={o.value} className={o.value === value ? 'active' : ''} aria-pressed={o.value === value} title={o.hint} onClick={() => onChange(o.value)}>{o.label}</button>)}
    </div>
  );
}

/**
 * Pick one of many. Segmented is for two to four short labels; anything longer, or anything with a
 * colour chip, is a ChoiceGrid. Both are toggle buttons (aria-pressed), never radios in disguise.
 */
export function ChoiceGrid<T extends string | number>({ value, options, onChange, label }: {
  value: T; options: { value: T; label: string; hint?: string; swatch?: ReactNode; disabled?: boolean }[]; onChange: (v: T) => void; label?: string;
}) {
  return (
    <div className={styles.choices} data-testid="choices" role="group" aria-label={label}>
      {options.map((o) => (
        <button key={String(o.value)} className={o.value === value ? 'active' : ''} aria-pressed={o.value === value} disabled={o.disabled} title={o.hint ?? o.label} onClick={() => onChange(o.value)}>
          {o.swatch}<span>{o.label}</span>
        </button>
      ))}
    </div>
  );
}

/** Colour chip for a ChoiceGrid option; `transparent` draws an outline-only box. */
export function Swatch({ color }: { color: string }) {
  return <i className={styles.swatch} style={{ background: color }} />;
}

/** A checkbox drawn as a pill. */
export function Chip({ label, on, onChange }: { label: string; on: boolean; onChange: () => void }) {
  return (
    <label className={`${styles.chip} ${on ? styles.chipOn : ''}`}>
      <input type="checkbox" checked={on} onChange={onChange} />{label}
    </label>
  );
}

const trim = (v: number) => (Math.abs(v) >= 10 ? v.toFixed(0) : Math.abs(v) >= 1 ? v.toFixed(2) : v.toFixed(3)).replace(/\.?0+$/, '') || '0';

function toHex(c: string): string {
  if (/^#[0-9a-f]{6}$/i.test(c)) return c;
  if (/^#[0-9a-f]{3}$/i.test(c)) return '#' + [...c.slice(1)].map((x) => x + x).join('');
  return '#000000';
}
