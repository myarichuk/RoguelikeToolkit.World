import { useEffect, useRef, useState } from 'react';
import { useApp, useViewModel } from './hooks';
import { TextField } from './controls';

export function TopBar() {
  const { vms } = useApp();
  const vm = vms.shell;
  const s = useViewModel(vm);
  const dirty = s.undo.dirty;
  return (
    <header className="topbar">
      <div className="brand">World Map Editor</div>
      <div className="menu" role="group" aria-label="File">
        <button onClick={vm.newBlank} title="Blank ocean to paint on">New</button>
        <button onClick={() => void vm.open()} title="Ctrl+O">Open…</button>
        <button className={dirty ? 'dirty' : ''} onClick={() => void vm.save()} title="Ctrl+S">Save{dirty && <span className="badge">Unsaved</span>}</button>
        <ExportMenu />
      </div>
      <div className="menu" role="group" aria-label="History">
        <button onClick={vm.undo} disabled={!s.undo.canUndo} title={`Undo ${s.undo.undoLabel ?? ''} (Ctrl+Z)`} aria-label="Undo">↶</button>
        <button onClick={vm.redo} disabled={!s.undo.canRedo} title={`Redo ${s.undo.redoLabel ?? ''} (Ctrl+Shift+Z)`} aria-label="Redo">↷</button>
      </div>
      <div className="title"><TextField value={s.title} onCommit={vm.setTitle} placeholder="Map title" /></div>
    </header>
  );
}

/** One disclosure for the three exports. Esc and outside clicks close it; arrows move between items. */
function ExportMenu() {
  const { vms } = useApp();
  const vm = vms.shell;
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', away);
    root.current?.querySelector<HTMLElement>('[role=menuitem]')?.focus();
    return () => document.removeEventListener('mousedown', away);
  }, [open]);
  const run = (f: () => void | Promise<void>) => () => { setOpen(false); trigger.current?.focus(); void f(); };
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') { setOpen(false); trigger.current?.focus(); return; }
    const d = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const items = [...root.current!.querySelectorAll<HTMLElement>('[role=menuitem]')];
    items[(items.indexOf(document.activeElement as HTMLElement) + d + items.length) % items.length]?.focus();
  };
  return (
    <div className="dropdown" ref={root} onKeyDown={onKey}>
      <button ref={trigger} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>Export ▾</button>
      {open && (
        <div className="menu-pop" role="menu" aria-label="Export">
          <button role="menuitem" onClick={run(vm.exportSvg)} title="Vector export (Inkscape layers)">SVG</button>
          <button role="menuitem" onClick={run(vm.exportPng)} title="Current view as PNG">PNG</button>
          <button role="menuitem" onClick={run(vm.exportWkt)} title="Rivers, towns, deposits and named areas as a CSV with WKT geometry (longitude/latitude), for QGIS or GIS tools. Includes your edits and names.">WKT (GIS)</button>
        </div>
      )}
    </div>
  );
}
