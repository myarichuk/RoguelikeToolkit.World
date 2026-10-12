import { CLIMATE_LAYERS, climateModes, HEIGHT_MODES, TOOLS } from '../viewmodels/ToolsViewModel';
import { useApp, useViewModel } from './hooks';
import { ChoiceGrid, Section, Select, Slider } from './controls';
import { BIOME_HINTS, LANDUSE_HINTS, TERRAIN_HINTS } from './glossary';
import { ToolIcon } from './icons';
import type { ToolId } from '../engine/tools/Tool';
import { LAND_USES } from '../engine/world/landuse';

const GROUPS: { name: string; tools: ToolId[] }[] = [
  { name: 'Navigate', tools: ['inspect', 'select', 'navigate'] },
  { name: 'Paint', tools: ['terrain', 'biome', 'landuse', 'climate'] },
  { name: 'Place', tools: ['stamp', 'text', 'river'] },
];

export function Toolbar() {
  const { vms } = useApp();
  const s = useViewModel(vms.tools);
  return (
    <nav className="toolbar" aria-label="Tools">
      {GROUPS.map((g) => (
        <div key={g.name} className="tool-group" role="group" aria-label={g.name}>
          <span className="group-name" aria-hidden="true">{g.name}</span>
          {g.tools.map((id) => {
            const t = TOOLS.find((x) => x.id === id)!;
            return (
              <button key={t.id} className={s.tool === t.id ? 'active' : ''} aria-pressed={s.tool === t.id} aria-keyshortcuts={t.key} onClick={() => vms.tools.setTool(t.id)} title={`${t.label} (${t.key}) — ${t.hint}`}>
                <ToolIcon id={t.id} />
                <span className="label">{t.label}</span>
              </button>
            );
          })}
        </div>
      ))}
    </nav>
  );
}

/** Options of the active tool. */
export function ToolOptionsPanel() {
  const { vms } = useApp();
  const vm = vms.tools;
  const s = useViewModel(vm);
  const o = s.options;
  const info = TOOLS.find((t) => t.id === s.tool)!;
  const brush = s.tool === 'terrain' || s.tool === 'biome' || s.tool === 'climate';
  const sized = brush || s.tool === 'landuse';
  return (
    <Section title={info.label}>
      <p className="hint">{info.hint}</p>
      {(s.tool === 'inspect' || s.tool === 'select') && <p className="hint">Filters in the <b>View</b> tab decide what can be picked. Picked features, stamps and labels are edited here.</p>}
      {sized && <Slider label="Radius" value={o.radius} min={2} max={200} step={1} onChange={(radius) => vm.setOptions({ radius })} hint="Brush size in map units. [ and ] change it from the keyboard." />}
      {s.tool === 'terrain' && (
        <>
          <Slider label="Strength" value={o.strength} min={0.005} max={0.2} step={0.005} onChange={(strength) => vm.setOptions({ strength })} hint="Height added per dab at the brush center (raise/lower), or the blend factor for smooth/flatten/roughen." />
          <ChoiceGrid label="Terrain brush" value={o.heightMode} options={HEIGHT_MODES.map((m) => ({ value: m.id, label: m.label, hint: TERRAIN_HINTS[m.id] }))} onChange={(heightMode) => vm.setOptions({ heightMode })} />
        </>
      )}
      {s.tool === 'climate' && (
        <>
          <Slider label="Strength" value={o.strength} min={0.005} max={0.2} step={0.005} onChange={(strength) => vm.setOptions({ strength })} hint="How much one dab adds at the brush center, as a share of full scale. Strokes accumulate." />
          <ChoiceGrid label="Climate layer" value={o.climateLayer} options={CLIMATE_LAYERS.map((l) => ({ value: l.id, label: l.label, hint: l.hint }))} onChange={(climateLayer) => vm.setOptions({ climateLayer })} />
          <ChoiceGrid label="Climate brush" value={o.climateMode} options={climateModes(o.climateLayer).map((m) => ({ value: m.id, label: m.label, hint: m.hint }))} onChange={(climateMode) => vm.setOptions({ climateMode })} />
          <p className="hint">Red/blue tint: warmer/cooler. Green/ochre: wetter/drier. The offset is local and literal: it is added after winds, orographic rain and ocean currents, so a warm sea patch does not move the gyres.</p>
        </>
      )}
      {s.tool === 'biome' && (
        <ChoiceGrid label="Biome" value={o.biome} onChange={(biome) => vm.setOptions({ biome })}
          options={s.biomes.map((b) => ({ value: b.id, label: b.name, hint: BIOME_HINTS[b.name] ?? b.name, swatch: <i style={{ background: s.biomeColors[String(b.id)] ?? b.vivid }} /> }))} />
      )}
      {s.tool === 'landuse' && (
        <>
          <ChoiceGrid label="Land use" value={o.landUse} onChange={(landUse) => vm.setOptions({ landUse })}
            options={[
              { value: 0, label: 'Clear', hint: LANDUSE_HINTS.clear, swatch: <i style={{ background: 'transparent' }} /> },
              ...LAND_USES.map((u) => ({ value: u.id, label: u.name, hint: u.hint, swatch: <i style={{ background: s.biomeColors[String(u.slot)] ?? '#999' }} /> })),
            ]} />
          <p className="hint">{LANDUSE_HINTS.note}</p>
        </>
      )}
      {brush && <PhysicsControls />}
      {s.tool === 'stamp' && (
        <>
          <Slider label="Scale" value={o.stampScale} min={0.3} max={3} step={0.05} onChange={(stampScale) => vm.setOptions({ stampScale })} />
          <Slider label="Scatter" value={o.scatter} min={0} max={40} step={1} onChange={(scatter) => vm.setOptions({ scatter })} format={(v) => (v ? `${v} mu` : 'off')} hint="Spacing of a sprayed run of decals. Off places one decal per click." />
          {o.scatter > 0 && <Slider label="Spray radius" value={o.radius} min={4} max={120} step={1} onChange={(radius) => vm.setOptions({ radius })} />}
          <AssetPalette />
        </>
      )}
      {s.tool === 'text' && (
        <Select label="Style" value={o.labelStyle} options={s.labelStyles.map((k) => ({ value: k, label: k }))} onChange={(labelStyle) => vm.setOptions({ labelStyle })} />
      )}
    </Section>
  );
}

/**
 * Brush strokes only repaint pixels; this is what makes them matter. It folds the strokes into
 * per-tile edits and re-derives climate, erosion, rivers, lakes and biomes from them.
 */
function PhysicsControls() {
  const { vms } = useApp();
  const s = useViewModel(vms.tools);
  const off = !s.physicsReady || s.busy;
  return (
    <div className="physics">
      <h4 title="Physics re-derives climate, erosion, rivers, lakes and biomes from your strokes.">Physics</h4>
      <p className="hint">Strokes only repaint until you apply them. Sculpt lets erosion reshape your terrain; Fix keeps it exactly as drawn. Rivers, lakes and biomes follow.</p>
      <div className="chips">
        <button disabled={off} onClick={() => void vms.tools.applyPhysics('sculpt')} title="Re-derive the world from your strokes; erosion and rain shadows act on them">Apply (sculpt)</button>
        <button disabled={off} onClick={() => void vms.tools.applyPhysics('fix')} title="Re-derive the world, keeping the heights you drew exactly">Apply (fix)</button>
        <button disabled={off} onClick={() => void vms.tools.applyPhysics('revert')} title="Discard every terrain, biome and climate edit and return to the generated world">Revert</button>
      </div>
      {!s.physicsReady && <p className="hint">Generate a world first: this map has no planet behind it.</p>}
    </div>
  );
}

export function AssetPalette() {
  const { vms } = useApp();
  const s = useViewModel(vms.tools);
  const cats = [...new Set(s.assets.map((a) => a.category))];
  return (
    <div className="assets">
      {cats.map((c) => (
        <div key={c}>
          <h4>{c}</h4>
          <div className="grid">
            {s.assets.filter((a) => a.category === c).map((a) => (
              <button key={a.id} className={s.options.asset === a.id ? 'active' : ''} aria-pressed={s.options.asset === a.id} title={a.name} onClick={() => vms.tools.pickAsset(a.id)}>
                <img src={a.thumb} alt={a.name} draggable={false} />
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
