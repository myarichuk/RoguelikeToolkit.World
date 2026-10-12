import { useMemo } from 'react';
import { useApp, useViewModel } from './hooks';
import { NumberField, Section, Slider, Toggle } from './controls';
import { CLIMATE_MODEL_DEFAULTS, EARTH } from '../engine/world/protocol';
import { MODEL_HINTS, PLANET_HINTS, WORLD_HINTS } from './glossary';

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
        <div className="name-row">
          <NumberField label="Seed" value={s.seed} onChange={vm.setSeed} min={0} hint={WORLD_HINTS.seed} />
          <button onClick={vm.randomSeed} title="Random seed" aria-label="Random seed">⚄</button>
        </div>
        <Slider label="Detail" value={s.size} min={3} max={7} step={1} onChange={vm.setSize} format={(v) => `${10 * 4 ** v + 2} tiles`} hint={WORLD_HINTS.detail} />
        <Slider label="Plates" value={s.plates} min={2} max={40} step={1} onChange={vm.setPlates} hint={WORLD_HINTS.plates} />
        <Slider label="Decal density" value={s.density} min={0} max={2} step={0.05} onChange={vm.setDensity} hint={WORLD_HINTS.density} />
      </Section>
      <Section title="Planet" collapsible defaultOpen={false} summary={same(s, EARTH) ? 'Earth' : 'custom'} hint="Astronomy of the world: how the star lights it. Changes the climate, so biomes, rivers and deserts all move.">
        <p className="hint">Takes effect on the next Generate. Earth is 23.4° tilt, flux 1, spin 1.</p>
        <Slider label="Axial tilt" value={s.obliquity} min={0} max={90} step={0.5} onChange={vm.setObliquity} format={(v) => `${v}°`} hint={PLANET_HINTS.obliquity} />
        <Slider label="Stellar flux" value={s.flux} min={0.6} max={1.4} step={0.01} onChange={vm.setFlux} format={(v) => `${v.toFixed(2)}× Earth`} hint={PLANET_HINTS.flux} />
        <Slider label="Spin" value={s.rotation} min={0.25} max={4} step={0.05} onChange={vm.setRotation} format={(v) => `${v.toFixed(2)}× Earth`} hint={PLANET_HINTS.rotation} />
        <button className="wide" onClick={vm.resetPlanet}>Earth</button>
      </Section>
      <Section title="Climate model" collapsible defaultOpen={false} summary={same(s, CLIMATE_MODEL_DEFAULTS) ? 'calibrated' : 'custom'} hint="Which climate physics the generator runs. The defaults are the calibrated ones; changing them changes every biome, river and desert.">
        <p className="hint">Takes effect on the next Generate. A saved map keeps the settings it was generated with.</p>
        <Toggle label="Re-run climate after erosion" on={s.climateRefresh} onChange={() => vm.setClimateRefresh(!s.climateRefresh)} hint={MODEL_HINTS.climateRefresh} />
        <Slider label="Diagonal wind" value={s.meridionalWind} min={0} max={0.6} step={0.05} onChange={vm.setMeridionalWind} format={(v) => v.toFixed(2)} hint={MODEL_HINTS.meridionalWind} />
        <Slider label="Calm belts" value={s.windCellBlend} min={0} max={1} step={0.05} onChange={vm.setWindCellBlend} format={(v) => (v === 0 ? 'off' : v.toFixed(2))} hint={MODEL_HINTS.windCellBlend} />
        <Slider label="Sea warmth" value={s.oceanHeat} min={0} max={2} step={0.1} onChange={vm.setOceanHeat} format={(v) => `${v.toFixed(1)}×`} hint={MODEL_HINTS.oceanHeat} />
        <Slider label="Sea rain" value={s.oceanRain} min={0} max={4} step={0.25} onChange={vm.setOceanRain} format={(v) => v.toFixed(2)} hint={MODEL_HINTS.oceanRain} />
        <Slider label="Wind erosion" value={s.aeolian} min={0} max={8} step={1} onChange={vm.setAeolian} format={(v) => (v === 0 ? 'off' : `${v} passes`)} hint={MODEL_HINTS.aeolian} />
        <button className="wide" onClick={vm.resetModel}>Calibrated defaults</button>
      </Section>
      <Section title="Cultures" collapsible defaultOpen={false} summary={s.styles.length ? `${s.styles.length} selected` : 'default mix'} hint={WORLD_HINTS.cultures}>
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

/** Pinned under the World tab so Generate never scrolls away from the settings that need it. */
export function GenerateFooter() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  return (
    <footer className="generate-footer">
      {s.stale && <span className="stale" role="status">● Settings changed since the last generate</span>}
      <button className="primary wide" disabled={s.busy} onClick={() => void vm.generate()}>{s.busy ? 'Working…' : 'Generate world'}</button>
    </footer>
  );
}

const same = <T extends object>(s: object, ref: T): boolean => (Object.keys(ref) as (keyof T)[]).every((k) => (s as T)[k] === ref[k]);
