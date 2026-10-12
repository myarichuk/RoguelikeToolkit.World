import { useState } from 'react';
import type { PaletteToken } from '../engine/content/types';
import { MAP_COLORS, MAP_NUMBERS } from '../viewmodels/ThemeEditorViewModel';
import { browserFiles } from './files';
import { useApp, useViewModel } from './hooks';
import { ColorField, Section, Select, Slider, TextField, Toggle } from './controls';
import { SHELL_HINTS, THEME_HINTS, WORLD_HINTS } from './glossary';

/** Live theme editor: every control writes into the user overlay pack. */
export function ThemePanel() {
  const { vms } = useApp();
  const vm = vms.theme;
  const s = useViewModel(vm);
  const shell = useViewModel(vms.shell);
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
        <Select label="Theme" value={shell.themeId} options={shell.themes.map((th) => ({ value: th.id, label: `${th.name}${th.edited ? ' (edited)' : ''}` }))} onChange={vms.shell.setTheme} hint={SHELL_HINTS.theme} />
        <TextField label="Name" value={t.name} onCommit={vm.rename} hint={THEME_HINTS.name} />
        <p className="hint">Edits are stored in the <b>My edits</b> pack (non-destructive). Export it to share or ship a theme.</p>
      </Section>
      <CoastlineSection />
      <Section title="Map">
        {MAP_COLORS.map((c) => <ColorField key={c.key} label={c.label} value={t.map[c.key] as string} onChange={(v) => vm.setMap(c.key, v)} hint={THEME_HINTS[c.key]} />)}
        {MAP_NUMBERS.map((n) => <Slider key={n.key} label={n.label} value={t.map[n.key] as number} min={n.min} max={n.max} step={n.step} onChange={(v) => vm.setMap(n.key, v)} hint={THEME_HINTS[n.key]} />)}
      </Section>
      <Section title="Biome colors" hint={THEME_HINTS.biomes}>
        {Object.keys(t.biomes).map((id) => <ColorField key={id} label={s.biomeNames[id] ?? `#${id}`} value={t.biomes[id]} onChange={(v) => vm.setBiome(id, v)} hint={THEME_HINTS.biomes} />)}
      </Section>
      <Section title="Stamp inks" hint={THEME_HINTS.inks}>
        {(Object.keys(t.palette) as PaletteToken[]).filter((k) => t.palette[k] !== 'none').map((k) => <ColorField key={k} label={k} value={t.palette[k]} onChange={(v) => vm.setPalette(k, v)} hint={THEME_HINTS.inks} />)}
      </Section>
      <Section title="Typography" actions={<button onClick={vm.relabel} title="Re-run label placement with the new sizes">Re-place labels</button>}>
        <Select label="Label kind" value={kind} options={kinds.map((k) => ({ value: k, label: k }))} onChange={setKind} hint={THEME_HINTS.labelKind} />
        {ls && (
          <>
            <Select label="Font" value={ls.font} options={['serif', 'italic', 'caps'].map((f) => ({ value: f, label: f }))} onChange={(font) => vm.setLabel(kind, { font })} hint={THEME_HINTS.labelFont} />
            <Slider label="Size" value={ls.size} min={2} max={60} step={0.5} onChange={(size) => vm.setLabel(kind, { size })} hint={THEME_HINTS.labelSize} />
            <Slider label="Grow w/ size" value={ls.sizeExp ?? 0} min={0} max={1} step={0.05} onChange={(sizeExp) => vm.setLabel(kind, { sizeExp })} hint={THEME_HINTS.labelGrow} />
            <Slider label="Tracking" value={ls.spacing} min={-0.1} max={1.2} step={0.01} onChange={(spacing) => vm.setLabel(kind, { spacing })} hint={THEME_HINTS.labelTracking} />
            <Slider label="Halo" value={ls.haloWidth} min={0} max={6} step={0.1} onChange={(haloWidth) => vm.setLabel(kind, { haloWidth })} hint={THEME_HINTS.labelHalo} />
            <Slider label="Min zoom" value={ls.minZoom ?? 0} min={0} max={8} step={0.1} onChange={(minZoom) => vm.setLabel(kind, { minZoom })} format={(v) => (v ? `${v.toFixed(1)}×` : 'always')} hint={THEME_HINTS.labelMinZoom} />
            <Toggle label="Uppercase" on={ls.uppercase} onChange={() => vm.setLabel(kind, { uppercase: !ls.uppercase })} hint={THEME_HINTS.labelUppercase} />
            <ColorField label="Fill" value={ls.fill} onChange={(fill) => vm.setLabel(kind, { fill })} hint={THEME_HINTS.labelFill} />
            <ColorField label="Halo color" value={ls.halo} onChange={(halo) => vm.setLabel(kind, { halo })} hint={THEME_HINTS.labelHaloColor} />
          </>
        )}
      </Section>
      <Section title="Editor chrome" hint={THEME_HINTS.chrome}>
        {Object.entries(t.ui ?? {}).map(([k, v]) => <ColorField key={k} label={k.replace(/^--/, '')} value={v} onChange={(c) => vm.setUi(k, c)} hint={THEME_HINTS.chrome} />)}
      </Section>
    </>
  );
}

/** Cosmetic only: how the hexagonal tile coast is drawn. Lives with the theme, not with generation. */
function CoastlineSection() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  return (
    <Section title="Coastline style" hint="Cosmetic only: how the hexagonal tile coast is drawn. It does not change the simulated world.">
      <Slider label="Warp" value={s.warp} min={0} max={16} step={0.25} onChange={(warp) => vm.setCoast({ warp })} format={(v) => `${v} mu`} hint={WORLD_HINTS.warp} />
      <Slider label="Detail noise" value={s.detail} min={0} max={0.2} step={0.005} onChange={(detail) => vm.setCoast({ detail })} hint={WORLD_HINTS.detailNoise} />
      <Slider label="Ripple spacing" value={s.ring} min={1} max={8} step={0.25} onChange={(ring) => vm.setCoast({ ring })} format={(v) => `${v} mu`} hint={WORLD_HINTS.ripple} />
    </Section>
  );
}
