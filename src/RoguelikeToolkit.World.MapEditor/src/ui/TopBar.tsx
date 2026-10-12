import { useApp, useViewModel } from './hooks';
import { TextField } from './controls';
import { SHELL_HINTS } from './glossary';

export function TopBar() {
  const { vms } = useApp();
  const vm = vms.shell;
  const s = useViewModel(vm);
  return (
    <header className="topbar">
      <div className="brand">World Map Editor</div>
      <div className="menu">
        <button onClick={vm.newBlank} title="Blank ocean to paint on">New</button>
        <button onClick={() => void vm.open()} title="Ctrl+O">Open…</button>
        <button onClick={() => void vm.save()} title="Ctrl+S">Save{s.undo.dirty ? ' •' : ''}</button>
        <span className="sep" />
        <button onClick={() => void vm.exportSvg()} title="Vector export (Inkscape layers)">Export SVG</button>
        <button onClick={vm.exportWkt} title="Rivers, towns, deposits and named areas as a CSV with WKT geometry (longitude/latitude), for QGIS or GIS tools. Includes your edits and names.">WKT</button>
        <button onClick={() => void vm.exportPng()} title="Current view as PNG">PNG</button>
        <span className="sep" />
        <button onClick={vm.undo} disabled={!s.undo.canUndo} title={`Undo ${s.undo.undoLabel ?? ''} (Ctrl+Z)`}>↶</button>
        <button onClick={vm.redo} disabled={!s.undo.canRedo} title={`Redo ${s.undo.redoLabel ?? ''} (Ctrl+Shift+Z)`}>↷</button>
      </div>
      <div className="title"><TextField value={s.title} onCommit={vm.setTitle} placeholder="Map title" /></div>
      <label className="theme-pick" title={SHELL_HINTS.theme}>
        Theme
        <select value={s.themeId} onChange={(e) => vm.setTheme(e.target.value)}>
          {s.themes.map((t) => <option key={t.id} value={t.id}>{t.name}{t.edited ? ' (edited)' : ''}</option>)}
        </select>
      </label>
      {s.busy && <div className="busy"><span className="spinner" />{s.busy}</div>}
    </header>
  );
}
