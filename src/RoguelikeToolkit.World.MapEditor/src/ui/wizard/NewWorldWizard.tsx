import { useEffect, useRef } from 'react';
import { CulturePicker } from '../GeneratePanel';
import { Chip, Select, Slider, Toggle } from '../controls';
import { useApp, useViewModel } from '../hooks';
import { DETAIL_LEVELS, estimateGenerateMs, formatEstimate, matchPlanetPreset, PLANET_PRESETS, TECTONIC_PRESETS, tilesOf } from '../../engine/world/presets';
import { GenerateViewModel } from '../../viewmodels/GenerateViewModel';
import { WIZARD_STEPS } from '../../viewmodels/WizardViewModel';
import { PlanetGlyph, PlaceMap, PlatesDiagram, TiltDiagram } from './Diagrams';
import styles from './NewWorldWizard.module.css';

const formLabel = (name: string) => name.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();

/** The "new world" wizard: planet, land, peoples, realms, look, then create. Esc closes it; nothing is generated until Create. */
export function NewWorldWizard() {
  const { vms } = useApp();
  const w = useViewModel(vms.wizard);
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => { if (w.open) root.current?.focus(); }, [w.open, w.step]);
  if (!w.open) return null;
  const last = w.step === WIZARD_STEPS.length - 1;
  const Step = [PlanetStep, LandStep, PeoplesStep, RealmsStep, LookStep, ReviewStep][w.step];
  return (
    <div className={styles.backdrop} onMouseDown={(e) => { if (e.target === e.currentTarget) vms.wizard.close(); }}>
      <div ref={root} className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="wiz-title" tabIndex={-1}
        onKeyDown={(e) => { if (e.key === 'Escape') vms.wizard.close(); }}>
        <header className={styles.head}>
          <h2 id="wiz-title">New world</h2>
          <nav className={styles.steps} aria-label="Steps">
            {WIZARD_STEPS.map((s, i) => (
              <button key={s.id} className={`${styles.step} ${i === w.step ? styles.stepOn : ''}`} aria-current={i === w.step ? 'step' : undefined} onClick={() => vms.wizard.goto(i)}>
                <span>{i + 1}</span>{s.label}
              </button>
            ))}
          </nav>
          <button className="mini" onClick={vms.wizard.randomize} title="Roll a planet, a land and a seed">Surprise me</button>
          <button className="mini" onClick={() => void vms.shell.open().then((opened) => { if (opened) vms.wizard.close(); })} title="Skip the wizard and open a saved .wmap world">Open existing…</button>
          <button className="mini" onClick={vms.wizard.close} aria-label="Close">✕</button>
        </header>
        <div className={styles.body}><Step /></div>
        <footer className={styles.foot}>
          <button onClick={vms.wizard.back} disabled={w.step === 0}>Back</button>
          <span className="grow" />
          {last
            ? <button className="primary" onClick={() => void vms.wizard.create()}>Create world</button>
            : <><button onClick={() => void vms.wizard.create()} title="Skip the rest and create the world with the current choices">Create now</button><button className="primary" onClick={vms.wizard.next}>Next</button></>}
        </footer>
      </div>
    </div>
  );
}

function PlanetStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const active = matchPlanetPreset({ obliquity: s.obliquity, flux: s.flux, rotation: s.rotation }, s);
  return (
    <div className={styles.cols}>
      <div>
        <h3>What kind of planet?</h3>
        <p className="hint">A preset sets the planet's tilt, sunlight and spin, and how its climate is modelled. Tune anything after.</p>
        <div className={styles.cards}>
          {PLANET_PRESETS.map((p) => (
            <button key={p.id} className={`${styles.card} ${active?.id === p.id ? styles.cardOn : ''}`} aria-pressed={active?.id === p.id} onClick={() => vm.applyPlanetPreset(p)}>
              <PlanetGlyph preset={p} />
              <strong>{p.label}</strong>
              <small>{p.blurb}</small>
            </button>
          ))}
        </div>
      </div>
      <div className={styles.side}>
        <div className={styles.figure}><TiltDiagram tilt={s.obliquity} flux={s.flux} spin={s.rotation} /></div>
        <Slider label="Axial tilt" value={s.obliquity} min={0} max={90} step={0.5} onChange={vm.setObliquity} format={(v) => `${v}°`} hint="How far the spin axis leans from the orbit. Low: no seasons and a wide tropics. High: violent seasons and sun at the poles." />
        <Slider label="Starlight" value={s.flux} min={0.6} max={1.4} step={0.01} onChange={vm.setFlux} format={(v) => `${v.toFixed(2)}× Earth`} hint="How much heat the star delivers. More melts the ice and dries the land; less pushes snow toward the equator." />
        <Slider label="Spin" value={s.rotation} min={0.25} max={4} step={0.05} onChange={vm.setRotation} format={(v) => `${v.toFixed(2)}× Earth`} hint="How fast the planet turns. Slow: weak winds. Fast: narrow alternating wet and dry bands." />
        <Slider label="Sea warmth" value={s.oceanHeat} min={0} max={2} step={0.1} onChange={vm.setOceanHeat} format={(v) => `${v.toFixed(1)}×`} hint="How much warmth the oceans carry inland." />
        <Slider label="Sea rain" value={s.oceanRain} min={0} max={4} step={0.25} onChange={vm.setOceanRain} format={(v) => v.toFixed(2)} hint="How much rain the sea feeds the coasts." />
        {active ? <ul className={styles.effects}>{active.effects.map((e) => <li key={e}>{e}</li>)}</ul> : <p className="hint">Custom settings. Pick a preset to see what it does.</p>}
        <button className="mini" onClick={() => vm.applyPlanetPreset(PLANET_PRESETS[0])}>Reset to Earthlike</button>
      </div>
    </div>
  );
}

function LandStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const w = useViewModel(vms.wizard);
  const active = TECTONIC_PRESETS.find((t) => t.plates === s.plates && t.aeolian === s.aeolian);
  const est = estimateGenerateMs(s.size, w.msPerTile);
  return (
    <div className={styles.cols}>
      <div>
        <h3>How is the land built?</h3>
        <p className="hint">Tectonic plates push up the mountains and open the seas. Fewer plates make bigger continents.</p>
        <div className={styles.cards}>
          {TECTONIC_PRESETS.map((t) => (
            <button key={t.id} className={`${styles.card} ${active?.id === t.id ? styles.cardOn : ''}`} aria-pressed={active?.id === t.id} onClick={() => vm.applyTectonics(t)}>
              <strong>{t.label}</strong><small>{t.blurb}</small><small className="muted">{t.plates} plates</small>
            </button>
          ))}
        </div>
        <h3>How detailed?</h3>
        <div className={styles.cards}>
          {DETAIL_LEVELS.map((d) => (
            <button key={d.size} className={`${styles.card} ${s.size === d.size ? styles.cardOn : ''}`} aria-pressed={s.size === d.size} onClick={() => vm.setSize(d.size)}>
              <strong>{d.label}</strong><small>{d.note}</small>
              <small className="muted">{tilesOf(d.size).toLocaleString()} tiles · {formatEstimate(estimateGenerateMs(d.size, w.msPerTile))}</small>
            </button>
          ))}
        </div>
        {s.size >= 7 && <p className={styles.warn} role="alert">Huge worlds can run for minutes and may run out of memory in the browser. Try Fine first.</p>}
        {s.size >= 6 && s.size < 7 && est > 15000 && <p className={styles.warn}>This device took {(w.msPerTile * 1000).toFixed(0)} µs per tile last time: Fine needs {formatEstimate(est)}.</p>}
      </div>
      <div className={styles.side}>
        <div className={styles.figure}><PlatesDiagram plates={s.plates} /></div>
        <Slider label="Plates" value={s.plates} min={2} max={40} step={1} onChange={vm.setPlates} hint="How many tectonic plates the crust is cut into." />
        <Slider label="Wind erosion" value={s.aeolian} min={0} max={8} step={1} onChange={vm.setAeolian} format={(v) => (v === 0 ? 'off' : `${v} passes`)} hint="How long wind wears the land down: more makes rounder hills and flatter basins." />
        <Toggle label="Re-run climate after erosion" on={s.climateRefresh} onChange={() => vm.setClimateRefresh(!s.climateRefresh)} hint="Re-derives rain and biomes on the eroded land: slower, more consistent." />
        <Slider label="Calm belts" value={s.windCellBlend} min={0} max={1} step={0.05} onChange={vm.setWindCellBlend} format={(v) => (v === 0 ? 'off' : v.toFixed(2))} hint="Blends the wind cells so the calm belts at the horse latitudes stay dry." />
        <Slider label="Diagonal wind" value={s.meridionalWind} min={0} max={0.6} step={0.05} onChange={vm.setMeridionalWind} format={(v) => v.toFixed(2)} hint="Tilts the prevailing winds toward the poles or equator." />
      </div>
    </div>
  );
}

function PeoplesStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const h = s.history;
  const key = GenerateViewModel.peopleKey;
  if (!s.peoples.length) return <p className="muted">Loading the world engine…</p>;
  return (
    <div>
      <Toggle label="Simulate history" on={h.enabled} onChange={() => vm.setHistoryEnabled(!h.enabled)} hint="Off leaves the map with no realms: settlements fall back to a plain site search, and there are no roads, ruins or holy sites." />
      {h.enabled && (
        <>
          <h3>Which peoples live here?</h3>
          <p className="hint">Realms are drawn from the peoples you tick. None ticked means humans and dwarves. Each people leans toward certain forms of government.</p>
          <div className={styles.cards}>
            {s.peoples.map((p) => {
              const on = h.roster.some((r) => r.folk === p.folk && r.culture === p.culture);
              const total = p.affinity.reduce((a, c) => a + c.weight, 0) || 1;
              return (
                <label key={key(p)} className={`${styles.card} ${on ? styles.cardOn : ''}`}>
                  <input type="checkbox" className={styles.hidden} checked={on} onChange={() => vm.togglePeople(key(p))} />
                  <strong>{p.folkName}</strong>
                  <small>{p.cultureName.split(' · ')[1] ?? p.cultureName}, living by {p.subsistence}</small>
                  {p.affinity.length > 0 ? (
                    <span className={styles.affinity} title={p.affinity.map((a) => `${formLabel(a.form)} ${a.weight}`).join(', ')}>
                      {p.affinity.map((a) => <i key={a.form} className={styles[`f${a.form}`]} style={{ flexGrow: a.weight / total }} />)}
                    </span>
                  ) : <small className="muted">government follows the land</small>}
                </label>
              );
            })}
          </div>
          <p className={styles.legend}><i className={styles.fKingdom} /> kingdom <i className={styles.fMerchantRepublic} /> merchant republic <i className={styles.fLooseAssociation} /> loose association <i className={styles.fHorde} /> horde</p>
          {h.roster.length > 0 && <button className="mini" onClick={vm.clearRoster}>Back to humans and dwarves</button>}
        </>
      )}
    </div>
  );
}

function RealmsStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const h = s.history;
  const key = GenerateViewModel.peopleKey;
  if (!h.enabled) return <p className="muted">History is off, so there are no realms. Turn it on in the Peoples step.</p>;
  const peopleOptions = [{ value: '|', label: 'Any (drawn)' }, ...s.peoples.map((p) => ({ value: key(p), label: p.cultureName }))];
  const formOptions = [{ value: '-1', label: 'Auto (affinity and land)' }, ...s.forms.map((f, i) => ({ value: String(i), label: formLabel(f) }))];
  return (
    <div className={styles.cols}>
      <div>
        <h3>How many realms, how long?</h3>
        <Slider label="Realms" value={h.maxPolities} min={1} max={24} step={1} onChange={vm.setMaxPolities} hint="How many realms are founded. Pinned founders count toward it." />
        <Slider label="Turns" value={h.turns} min={2} max={200} step={1} onChange={vm.setTurns} format={(v) => `${v} turns`} hint="How long history runs." />
        <Slider label="Years per turn" value={h.years} min={1} max={100} step={1} onChange={vm.setYears} format={(v) => `${v} y`} hint="Longer turns age people and realms faster." />
        <p className="hint">History runs {h.turns * h.years} years. It decides where the cities, roads, ruins, holy sites and battlefields are.</p>
        {s.historyFeatures.length > 0 && <HistoryFeatures />}
      </div>
      <div className={styles.side}>
        <h3>Pin realms <small className="muted">{h.founders.length}/{h.maxPolities}</small></h3>
        <p className="hint">Optional: fix a realm's people, government and capital. Anything left blank is drawn.</p>
        {h.founders.map((f, i) => (
          <div key={i} className={styles.founder}>
            <div className={styles.row}><strong>Realm {i + 1}</strong><button className="mini" onClick={() => vm.removeFounder(i)}>Remove</button></div>
            <Select label="People" value={`${f.folk}|${f.culture}`} options={peopleOptions} onChange={(v) => vm.setFounderPeople(i, v === '|' ? '' : v)} />
            <Select label="Government" value={String(f.form)} options={formOptions} onChange={(v) => vm.updateFounder(i, { form: Number(v) })} />
            <PlaceMap lat={f.lat} lon={f.lon} onPick={(lat, lon) => vm.updateFounder(i, { lat, lon })} />
            <div className={styles.row}>
              <small className="muted">{f.lat === null || f.lon === null ? 'Capital: best site found' : `Capital near ${f.lat}°, ${f.lon}°`}</small>
              <button className="mini" disabled={f.lat === null} onClick={() => vm.updateFounder(i, { lat: null, lon: null })}>Clear</button>
            </div>
          </div>
        ))}
        <button disabled={h.founders.length >= h.maxPolities} onClick={vm.addFounder}>Pin a realm</button>
      </div>
    </div>
  );
}

/** What each expansion feature adds, by Core's flag name; a flag the editor does not know yet shows its name alone. */
const FEATURE_HINTS: Record<string, string> = {
  Persons: 'Named rulers, heirs and office holders, with their bonds and offices.',
  Battles: 'Wars are fought as battles, with generals, tactics and terrain, instead of a coin flip.',
  Sieges: 'Walled places are besieged after a won battle; relief armies; forts on the borders.',
  Logistics: 'Supply over roads, rivers and sea lanes; road safety, waystations, winter quarters.',
  Commerce: 'Caravans and traffic on the roads, merchant princes who fund wars, smugglers and privateers.',
  Outlaws: 'Outlaw bands that hide beside the roads and raid caravans until hanged or pardoned.',
  Intrigue: 'Spymasters plot: agents, bribes, sabotage, assassinations and sponsored bands.',
};

/** ", with persons and battles" for the review; nothing for Core's default. */
const featureSummary = (features: number | undefined, offered: readonly { name: string; bit: number }[]): string => {
  if (features === undefined || features < 0) return '';
  const on = offered.filter((f) => features & f.bit).map((f) => f.name.toLowerCase());
  return on.length ? `, with ${on.join(', ')}` : ', no expansion';
};

/** The history expansion's features: unticked and untouched means Core's default. */
function HistoryFeatures() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const chosen = s.history.features;
  return (
    <>
      <h3>What does history tell?</h3>
      <p className="hint">{chosen === undefined || chosen < 0 ? 'Nothing ticked: the engine\'s default. ' : ''}Each one adds detail to history: slower to run, fuller to read (people, battles and outlaws on the History tab and the map).</p>
      <div role="group" aria-label="History features" data-testid="history-features">
        {s.historyFeatures.map((f) => (
          <Toggle key={f.bit} label={f.name.replace(/([a-z])([A-Z])/g, '$1 $2')} on={chosen !== undefined && chosen >= 0 && (chosen & f.bit) !== 0} onChange={() => vm.toggleFeature(f.bit)} hint={FEATURE_HINTS[f.name]} />
        ))}
      </div>
      <div className={styles.row}>
        <button className="mini" onClick={vm.allFeatures}>All</button>
        <button className="mini" disabled={chosen === undefined} onClick={vm.defaultFeatures}>Engine default</button>
      </div>
    </>
  );
}

function LookStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const shell = useViewModel(vms.shell);
  const s = useViewModel(vm);
  return (
    <div className={styles.cols}>
      <div>
        <h3>Which map style?</h3>
        <div className={styles.cards}>
          {shell.themes.map((t) => (
            <button key={t.id} className={`${styles.card} ${shell.themeId === t.id ? styles.cardOn : ''}`} aria-pressed={shell.themeId === t.id} onClick={() => vms.shell.setTheme(t.id)}>
              <strong>{t.name}</strong>
            </button>
          ))}
        </div>
        <h3>Coastline</h3>
        <Slider label="Roughness" value={s.warp} min={0} max={20} step={0.5} onChange={(v) => vm.setCoast({ warp: v })} hint="How far coasts are wobbled from the physical shore. 0 follows the tile grid exactly." />
        <Slider label="Fine detail" value={s.detail} min={0} max={0.2} step={0.005} onChange={(v) => vm.setCoast({ detail: v })} format={(v) => v.toFixed(3)} hint="Small-scale noise along the coast: bays and headlands." />
        <Slider label="Ripple spacing" value={s.ring} min={1} max={8} step={0.25} onChange={(v) => vm.setCoast({ ring: v })} hint="Distance between the decorative rings in the water." />
        <Slider label="Decal density" value={s.density} min={0} max={2} step={0.05} onChange={vm.setDensity} hint="How thickly trees, hills and mountains are drawn." />
      </div>
      <div className={styles.side}>
        <h3>Name styles</h3>
        <p className="hint">Each people you chose names its places in one of these; land no realm claims is named by landmass.</p>
        <CulturePicker />
      </div>
    </div>
  );
}

function ReviewStep() {
  const { vms } = useApp();
  const vm = vms.generate;
  const s = useViewModel(vm);
  const w = useViewModel(vms.wizard);
  const planet = matchPlanetPreset({ obliquity: s.obliquity, flux: s.flux, rotation: s.rotation }, s);
  const land = TECTONIC_PRESETS.find((t) => t.plates === s.plates && t.aeolian === s.aeolian);
  const detail = DETAIL_LEVELS.find((d) => d.size === s.size);
  const h = s.history;
  const rows: [string, string, number][] = [
    ['Planet', planet ? planet.label : `custom: tilt ${s.obliquity}°, ${s.flux.toFixed(2)}× light, ${s.rotation.toFixed(2)}× spin`, 0],
    ['Land', `${land ? land.label : 'custom'}, ${s.plates} plates, ${detail?.label ?? s.size} detail (${tilesOf(s.size).toLocaleString()} tiles)`, 1],
    ['Peoples', h.enabled ? (h.roster.length ? h.roster.map((r) => s.peoples.find((p) => p.folk === r.folk)?.folkName ?? r.folk).join(', ') : 'humans and dwarves') : 'no history', 2],
    ['Realms', h.enabled ? `${h.maxPolities} realms over ${h.turns * h.years} years${h.founders.length ? `, ${h.founders.length} pinned` : ''}${featureSummary(h.features, s.historyFeatures)}` : 'none', 3],
    ['Look', `${vms.shell.state.themes.find((t) => t.id === vms.shell.state.themeId)?.name ?? vms.shell.state.themeId}, decals ${s.density.toFixed(2)}×`, 4],
  ];
  return (
    <div className={styles.review}>
      <h3>Ready</h3>
      <dl>{rows.map(([k, v, step]) => <div key={k}><dt><button className="disclosure" onClick={() => vms.wizard.goto(step)}>{k}</button></dt><dd>{v}</dd></div>)}</dl>
      <div className={styles.row}>
        <label>Seed <input type="number" min={0} value={s.seed} onChange={(e) => vm.setSeed(Number(e.target.value))} /></label>
        <button className="mini" onClick={vm.randomSeed} title="Random seed">⚄ New seed</button>
      </div>
      <p className="hint">The same seed with the same choices always gives the same world. Generating takes {formatEstimate(estimateGenerateMs(s.size, w.msPerTile))} on this device.</p>
      <Chip label="Open this wizard when the editor starts" on={w.showOnStart} onChange={() => vms.wizard.setShowOnStart(!w.showOnStart)} />
    </div>
  );
}
