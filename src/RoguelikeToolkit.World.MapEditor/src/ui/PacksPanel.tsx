import { useApp, useViewModel } from './hooks';
import { Section } from './controls';
import { SHELL_HINTS } from './glossary';

export function PacksPanel() {
  const { vms } = useApp();
  const vm = vms.packs;
  const s = useViewModel(vm);
  return (
    <Section title="Content packs" actions={<>
      <button onClick={() => void vm.importFile()} title={SHELL_HINTS.packImport}>Import…</button>
      <button onClick={() => void vm.importUrl('packs/example-pack.json')} title={SHELL_HINTS.packExample}>Example</button>
    </>}>
      <p className="hint">Packs add themes, biomes, stamp assets (procedural presets or SVG), placement rules and fonts. Later packs override earlier ones by id.</p>
      {s.error && <pre className="error" onClick={vm.dismissError}>{s.error}</pre>}
      <ul className="packs">
        {s.packs.map((p) => (
          <li key={p.id} className={p.enabled ? '' : 'off'}>
            <div className="grow">
              <b>{p.name}</b> <span className="muted">{p.id} · v{p.version}{p.author && ` · ${p.author}`}</span>
              <div className="small muted">{p.summary}</div>
              {p.description && <div className="small">{p.description}</div>}
            </div>
            {p.removable && <>
              <button onClick={() => vm.toggle(p.id)} title={SHELL_HINTS.packToggle}>{p.enabled ? 'Disable' : 'Enable'}</button>
              <button onClick={() => vm.remove(p.id)} title="Remove this pack">×</button>
            </>}
            {p.id === 'user' && <button onClick={vm.exportUser} title={SHELL_HINTS.packExport}>Export</button>}
          </li>
        ))}
      </ul>
    </Section>
  );
}
