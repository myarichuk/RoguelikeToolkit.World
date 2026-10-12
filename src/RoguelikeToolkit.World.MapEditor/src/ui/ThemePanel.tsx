import { useState } from 'react';
import type { PaletteToken } from '../engine/content/types';
import { MAP_COLORS, MAP_NUMBERS } from '../viewmodels/ThemeEditorViewModel';
import { browserFiles } from './files';
import { useApp, useViewModel } from './hooks';
import { ColorField, Section, Select, Slider, TextField, Toggle } from './controls';

/** Live theme editor: every control writes into the user overlay pack. */
export function ThemePanel() {
  const { vms } = useApp();
  const vm = vms.theme;
  const s = useViewModel(vm);
  const t = s.theme;
  const kinds = Object.keys(t.labels);
  const [kind, setKind] = useState(kinds[0] ?? 'city');
  const ls = t.labels[kind];
  return (
    <>
      <Section title="Theme" actions={<>
        <button onClick={vm.duplicate} title="Copy as a new theme">Duplicate</button>
        {s.edited && <button onClick={vm.reset} title="Drop your edits to this theme">Reset</button>}
        <button onClick={() => { const b = vm.exportPack(); if (b) browserFiles.save(b, 'my-theme-pack.json'); }} disabled={!s.edited} title="Save your edits as a content pack">Export</button>
      </>}>
        <TextField label="Name" value={t.name} onCommit={vm.rename} />
        <p className="hint">Edits are stored in the <b>My edits</b> pack (non-destructive). Export it to share or ship a theme.</p>
      </Section>
      <Section title="Map">
        {MAP_COLORS.map((c) => <ColorField key={c.key} label={c.label} value={t.map[c.key] as string} onChange={(v) => vm.setMap(c.key, v)} />)}
        {MAP_NUMBERS.map((n) => <Slider key={n.key} label={n.label} value={t.map[n.key] as number} min={n.min} max={n.max} step={n.step} onChange={(v) => vm.setMap(n.key, v)} />)}
      </Section>
      <Section title="Biome colors">
        {Object.keys(t.biomes).map((id) => <ColorField key={id} label={s.biomeNames[id] ?? `#${id}`} value={t.biomes[id]} onChange={(v) => vm.setBiome(id, v)} />)}
      </Section>
      <Section title="Stamp inks">
        {(Object.keys(t.palette) as PaletteToken[]).filter((k) => t.palette[k] !== 'none').map((k) => <ColorField key={k} label={k} value={t.palette[k]} onChange={(v) => vm.setPalette(k, v)} />)}
      </Section>
      <Section title="Typography" actions={<button onClick={vm.relabel} title="Re-run label placement with the new sizes">Re-place labels</button>}>
        <Select label="Label kind" value={kind} options={kinds.map((k) => ({ value: k, label: k }))} onChange={setKind} />
        {ls && (
          <>
            <Select label="Font" value={ls.font} options={['serif', 'italic', 'caps'].map((f) => ({ value: f, label: f }))} onChange={(font) => vm.setLabel(kind, { font })} />
            <Slider label="Size" value={ls.size} min={2} max={60} step={0.5} onChange={(size) => vm.setLabel(kind, { size })} />
            <Slider label="Grow w/ size" value={ls.sizeExp ?? 0} min={0} max={1} step={0.05} onChange={(sizeExp) => vm.setLabel(kind, { sizeExp })} />
            <Slider label="Tracking" value={ls.spacing} min={-0.1} max={1.2} step={0.01} onChange={(spacing) => vm.setLabel(kind, { spacing })} />
            <Slider label="Halo" value={ls.haloWidth} min={0} max={6} step={0.1} onChange={(haloWidth) => vm.setLabel(kind, { haloWidth })} />
            <Slider label="Min zoom" value={ls.minZoom ?? 0} min={0} max={8} step={0.1} onChange={(minZoom) => vm.setLabel(kind, { minZoom })} format={(v) => (v ? `${v.toFixed(1)}×` : 'always')} />
            <Toggle label="Uppercase" on={ls.uppercase} onChange={() => vm.setLabel(kind, { uppercase: !ls.uppercase })} />
            <ColorField label="Fill" value={ls.fill} onChange={(fill) => vm.setLabel(kind, { fill })} />
            <ColorField label="Halo color" value={ls.halo} onChange={(halo) => vm.setLabel(kind, { halo })} />
          </>
        )}
      </Section>
      <Section title="Editor chrome">
        {Object.entries(t.ui ?? {}).map(([k, v]) => <ColorField key={k} label={k.replace(/^--/, '')} value={v} onChange={(c) => vm.setUi(k, c)} />)}
      </Section>
    </>
  );
}
