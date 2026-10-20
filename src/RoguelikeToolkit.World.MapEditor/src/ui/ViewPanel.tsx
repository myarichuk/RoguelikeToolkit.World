import type { BaseMode, Layers } from '../engine/render/ViewSettings';
import { useApp, useViewModel } from './hooks';
import { ChoiceGrid, Section, Segmented, Slider, Toggle } from './controls';
import { CAPACITY_KINDS, type CapacityKind } from '../engine/world/physics/capacity';
import { CLIMATE_KINDS, climateDef, rampGradient, type ClimateKind } from '../engine/world/physics/climate';
import { BASE_HINTS, LAYER_HINTS } from './glossary';
import styles from './ViewPanel.module.css';

const LAYERS: { key: keyof Layers; label: string }[] = [
  { key: 'hillshade', label: 'Hillshade' }, { key: 'contours', label: 'Contours' }, { key: 'coastline', label: 'Coastline' },
  { key: 'ripples', label: 'Ripples' }, { key: 'rivers', label: 'Rivers' }, { key: 'roads', label: 'Roads' }, { key: 'stamps', label: 'Stamps' },
  { key: 'labels', label: 'Labels' }, { key: 'resources', label: 'Resource markers' }, { key: 'plateBoundaries', label: 'Plate boundaries' },
  { key: 'plateMotion', label: 'Plate motion' }, { key: 'graticule', label: 'Graticule' }, { key: 'climatePaint', label: 'Climate paint' }, { key: 'capacity', label: 'Land capacity' },
  { key: 'climate', label: 'Climate' }, { key: 'wind', label: 'Wind arrows' }, { key: 'currents', label: 'Current arrows' },
];

const BASES: { value: BaseMode; label: string }[] = [
  { value: 'styled', label: 'Map' }, { value: 'biomes', label: 'Biomes' }, { value: 'elevation', label: 'Elevation' }, { value: 'plates', label: 'Plates' },
];

export function ViewPanel() {
  const { vms } = useApp();
  const vm = vms.filters;
  const s = useViewModel(vm);
  const v = s.view;
  return (
    <>
      <Section title="Presets">
        <ChoiceGrid label="Presets" value={s.presets.find((p) => p.active)?.id ?? ''} options={s.presets.map((p) => ({ value: p.id, label: p.label }))} onChange={vm.applyPreset} />
      </Section>
      <Section title="Base">
        <Segmented value={v.base} options={BASES.map((b) => ({ ...b, hint: BASE_HINTS[b.value] }))} onChange={vm.setBase} />
        {v.base === 'plates' && <Slider label="Topography" value={v.plateTopo} min={0} max={1} step={0.05} onChange={vm.setPlateTopo} />}
        <div className={styles.toggles}>
          {LAYERS.map((l) => <Toggle key={l.key} label={l.label} on={v.layers[l.key]} onChange={() => vm.toggleLayer(l.key)} hint={LAYER_HINTS[l.key]} />)}
        </div>
        {v.layers.capacity && (
          <Segmented<CapacityKind> value={v.capacityKind} options={CAPACITY_KINDS.map((k) => ({ value: k.key, label: k.label, hint: k.hint }))} onChange={vm.setCapacityKind} />
        )}
        {v.layers.wind && v.layers.currents && (
          <p className="hint">Thin white lines with an open arrowhead are wind. Thick coloured arrows with a solid head are sea currents (red warm, blue cold, white neither).</p>
        )}
        {v.layers.climate && (
          <>
            <ChoiceGrid<ClimateKind> label="Climate field" value={v.climateKind} options={CLIMATE_KINDS.map((k) => ({ value: k.key, label: k.label, hint: k.hint }))} onChange={vm.setClimateKind} />
            <div className={styles.legend} title={climateDef(v.climateKind).hint}>
              <i style={{ background: rampGradient(climateDef(v.climateKind)) }} />
              <span>{climateDef(v.climateKind).legend[0]}</span><span>{climateDef(v.climateKind).legend[1]}</span>
            </div>
          </>
        )}
      </Section>
      <Section title="Features">
        <p className="hint">Hidden features are not drawn and cannot be picked.</p>
        <div className={styles.toggles}>
          {s.kinds.map((k) => <Toggle key={k.key} label={k.label} on={k.on} count={k.count} onChange={() => vm.toggleKind(k.key)} />)}
        </div>
      </Section>
      <Section title="Resources" actions={<><button onClick={() => vm.setAllDeposits(true)}>All</button><button onClick={() => vm.setAllDeposits(false)}>None</button></>}>
        <div className={styles.toggles}>
          {s.deposits.map((d) => (
            <Toggle key={d.key} label={d.label} on={d.on} count={d.count} onChange={() => vm.toggleDeposit(d.key)}
              swatch={<i className={styles.dep} style={{ background: d.color }}>{d.symbol}</i>} />
          ))}
        </div>
      </Section>
    </>
  );
}
