import { useMemo } from 'react';
import { useApp, useViewModel } from './hooks';
import { NumberField, Section, Slider, TextField } from './controls';
import { PLANET_HINTS, WORLD_HINTS } from './glossary';

export function GeneratePanel() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const byRace = useMemo(() => {
    const m = new Map<string, typeof s.available>();
    for (const o of s.available) { const l = m.get(o.race) ?? []; l.push(o); m.set(o.race, l); }
    return [...m.entries()];
  }, [s.available]);
  return (
    <>
      <Section title="World" hint="The seed and size fully determine the simulated planet (with the planet settings).">
        <TextField label="Title" value={s.title} onCommit={vm.setTitle} />
        <div className="name-row">
          <NumberField label="Seed" value={s.seed} onChange={vm.setSeed} min={0} hint={WORLD_HINTS.seed} />
          <button onClick={vm.randomSeed} title="Random seed">⚄</button>
        </div>
        <Slider label="Detail" value={s.size} min={3} max={7} step={1} onChange={vm.setSize} format={(v) => `${10 * 4 ** v + 2} tiles`} hint={WORLD_HINTS.detail} />
        <Slider label="Plates" value={s.plates} min={2} max={40} step={1} onChange={vm.setPlates} hint={WORLD_HINTS.plates} />
        <Slider label="Decal density" value={s.density} min={0} max={2} step={0.05} onChange={vm.setDensity} hint={WORLD_HINTS.density} />
        <button className="primary wide" disabled={s.busy} onClick={() => void vm.generate()}>{s.busy ? 'Working…' : 'Generate world'}</button>
      </Section>
      <Section title="Planet" hint="Astronomy of the world: how the star lights it. Changes the climate, so biomes, rivers and deserts all move.">
        <p className="hint">Takes effect on the next Generate. Earth is 23.4° tilt, flux 1, spin 1.</p>
        <Slider label="Axial tilt" value={s.obliquity} min={0} max={90} step={0.5} onChange={vm.setObliquity} format={(v) => `${v}°`} hint={PLANET_HINTS.obliquity} />
        <Slider label="Stellar flux" value={s.flux} min={0.6} max={1.4} step={0.01} onChange={vm.setFlux} format={(v) => `${v.toFixed(2)}× Earth`} hint={PLANET_HINTS.flux} />
        <Slider label="Spin" value={s.rotation} min={0.25} max={4} step={0.05} onChange={vm.setRotation} format={(v) => `${v.toFixed(2)}× Earth`} hint={PLANET_HINTS.rotation} />
        <button className="wide" onClick={vm.resetPlanet}>Earth</button>
      </Section>
      <Section title="Coastline style" hint="Cosmetic only: how the hexagonal tile coast is drawn. It does not change the simulated world.">
        <Slider label="Warp" value={s.warp} min={0} max={16} step={0.25} onChange={(warp) => vm.setCoast({ warp })} format={(v) => `${v} mu`} hint={WORLD_HINTS.warp} />
        <Slider label="Detail noise" value={s.detail} min={0} max={0.2} step={0.005} onChange={(detail) => vm.setCoast({ detail })} hint={WORLD_HINTS.detailNoise} />
        <Slider label="Ripple spacing" value={s.ring} min={1} max={8} step={0.25} onChange={(ring) => vm.setCoast({ ring })} format={(v) => `${v} mu`} hint={WORLD_HINTS.ripple} />
      </Section>
      <Section title={`Cultures (${s.styles.length || 'default'})`} hint={WORLD_HINTS.cultures}>
        <p className="hint">Each tectonic plate is named by one of these. Names come from the Core name generator.</p>
        <div className="cultures">
          {byRace.map(([race, list]) => (
            <div key={race}>
              <h4>{race.replace(/_/g, ' ')}</h4>
              {list.map((o) => (
                <label key={o.key} className={`chip ${s.styles.includes(o.key) ? 'on' : ''}`}>
                  <input type="checkbox" checked={s.styles.includes(o.key)} onChange={() => vm.toggleStyle(o.key)} />{o.label}
                </label>
              ))}
            </div>
          ))}
          {!byRace.length && <p className="muted">Loading the world engine…</p>}
        </div>
      </Section>
    </>
  );
}
