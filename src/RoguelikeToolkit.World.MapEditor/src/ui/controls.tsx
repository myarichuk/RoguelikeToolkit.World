import { useEffect, useState, type ReactNode } from 'react';

// Small, dumb, reusable controls. No state of their own beyond text-entry drafts.

export function Section({ title, children, actions, hint }: { title: string; children: ReactNode; actions?: ReactNode; hint?: string }) {
  return (
    <section className="section">
      <header><h3 title={hint}>{title}</h3>{actions && <div className="actions">{actions}</div>}</header>
      {children}
    </section>
  );
}

export function Slider({ label, value, min, max, step, onChange, format, hint }: {
  label: string; value: number; min: number; max: number; step: number; onChange: (v: number) => void; format?: (v: number) => string;
  /** Tooltip: one or two sentences saying what the term means. */
  hint?: string;
}) {
  return (
    <label className="row slider" title={hint}>
      <span>{label}</span>
      <input type="range" min={min} max={max} step={step} value={value} onChange={(e) => onChange(parseFloat(e.target.value))} />
      <output>{format ? format(value) : trim(value)}</output>
    </label>
  );
}

export function NumberField({ label, value, onChange, min, max, step = 1, hint }: { label: string; value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number; hint?: string }) {
  return (
    <label className="row" title={hint}>
      <span>{label}</span>
      <input type="number" value={value} min={min} max={max} step={step} onChange={(e) => { const v = parseFloat(e.target.value); if (Number.isFinite(v)) onChange(v); }} />
    </label>
  );
}

/** Commits on Enter/blur so every keystroke is not an undo step. */
export function TextField({ label, value, onCommit, placeholder }: { label?: string; value: string; onCommit: (v: string) => void; placeholder?: string }) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  const commit = () => { if (draft !== value) onCommit(draft); };
  const input = (
    <input type="text" value={draft} placeholder={placeholder} onChange={(e) => setDraft(e.target.value)} onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') commit(); if (e.key === 'Escape') setDraft(value); }} />
  );
  return label ? <label className="row"><span>{label}</span>{input}</label> : input;
}

export function Toggle({ label, on, onChange, swatch, count, hint }: { label: string; on: boolean; onChange: () => void; swatch?: ReactNode; count?: number; hint?: string }) {
  return (
    <label className={`toggle ${on ? 'on' : ''}`} title={hint}>
      <input type="checkbox" checked={on} onChange={onChange} />
      {swatch}
      <span className="grow">{label}</span>
      {count !== undefined && <span className="count">{count}</span>}
    </label>
  );
}

export function ColorField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <label className="row color">
      <span>{label}</span>
      <input type="color" value={toHex(value)} onChange={(e) => onChange(e.target.value)} />
      <code>{value}</code>
    </label>
  );
}

export function Select<T extends string>({ label, value, options, onChange, hint }: { label?: string; value: T; options: { value: T; label: string }[]; onChange: (v: T) => void; hint?: string }) {
  const sel = (
    <select value={value} onChange={(e) => onChange(e.target.value as T)}>
      {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
    </select>
  );
  return label ? <label className="row" title={hint}><span>{label}</span>{sel}</label> : sel;
}

export function Segmented<T extends string>({ value, options, onChange }: { value: T; options: { value: T; label: string; hint?: string }[]; onChange: (v: T) => void }) {
  return (
    <div className="segmented">
      {options.map((o) => <button key={o.value} className={o.value === value ? 'active' : ''} title={o.hint} onClick={() => onChange(o.value)}>{o.label}</button>)}
    </div>
  );
}

const trim = (v: number) => (Math.abs(v) >= 10 ? v.toFixed(0) : Math.abs(v) >= 1 ? v.toFixed(2) : v.toFixed(3)).replace(/\.?0+$/, '') || '0';

function toHex(c: string): string {
  if (/^#[0-9a-f]{6}$/i.test(c)) return c;
  if (/^#[0-9a-f]{3}$/i.test(c)) return '#' + [...c.slice(1)].map((x) => x + x).join('');
  return '#000000';
}
