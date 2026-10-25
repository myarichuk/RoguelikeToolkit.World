/// <reference lib="webworker" />
// The world service: RoguelikeToolkit.World.Core (browser-wasm) off the main thread. It owns the
// .NET runtime; the editor only ever sees plain data (protocol.ts). Rasterization and feature
// extraction run here too, in V8 — measurably faster than the Mono interpreter for per-pixel loops.
import { MAP_H, MAP_W, vecToMap } from '../engine/core/geo';
import { hash2 } from '../engine/core/rng';
import { projectClimate } from '../engine/world/physics/climate';
import { buildAdjacency, extractFeatures } from '../engine/world/features';
import { polityStyleIndex, tileNamingStyles } from '../engine/world/naming';
import { parseRealmNaming, realmNameRequest, realmTitle, type RealmNaming } from '../engine/world/history/realmNames';
import { buildRoads } from '../engine/world/history/roads';
import type { EventRecord, HistoryRecord, PolityRecord } from '../engine/world/history/record';
import type { HistoryInput } from '../engine/world/history/historySettlements';
import {
  CLIMATE_MODEL_DEFAULTS, DEFAULT_HISTORY, DEFAULT_STYLES, EARTH, snapshotTransfer, styleKey,
  type GenerateParams, type HistorySettings, type NameRequest, type NameStyle, type RecomputeParams, type WorkerRequest, type WorkerResponse, type WorldSnapshot,
} from '../engine/world/protocol';
import { rasterize } from '../engine/world/rasterize';
import { uniqueNamesSync } from '../engine/world/history/uniqueNames';

type Arr = ArrayLike<number>;

/** The [JSExport] surface of RoguelikeToolkit.World.MapEditor.Engine.WorldExports. */
interface EngineApi {
  Generate(seed: number, size: number, plates: number, obliquity: number, flux: number, rotation: number,
    climateRefresh: boolean, meridionalWind: number, windCellBlend: number, oceanHeat: number, oceanRain: number, aeolian: number): number;
  ApplyEdits(tiles: number[], heightDelta: number[], pinHeight: number[], meta: number[], temperature: number[], precipitation: number[]): number;
  TileVectors(): Arr;
  Faces(): Arr;
  Heights(): Arr;
  Biomes(): Arr;
  Plates(): Arr;
  PlateDrift(): Arr;
  Flow(): Arr;
  LakeDepth(): Arr;
  Climate(): Arr;
  LandCapacity(): Arr;
  SeaIce(): Arr;
  ClimateField(): Arr;
  Rivers(): Arr;
  HistoryRoads(): Arr;
  ConfigureHistory(enabled: boolean, turnCount: number, turnYears: number, maxPolities: number): void;
  ConfigureHistoryPeoples(folks: string[], cultures: string[]): void;
  ConfigureHistoryFounders(folks: string[], cultures: string[], forms: number[], lats: number[], lons: number[]): void;
  HistoryPeoples(): string[];
  HistoryFormNames(): string[];
  HistorySettlements(): Arr;
  HistoryPolities(): Arr;
  HistoryPolityKeys(): string[];
  HistoryOwnershipAt(turn: number): Arr;
  HistoryOwnershipBase(): Arr;
  HistoryTerritoryChanges(): Arr;
  HistoryInfo(): Arr;
  HistoryStrings(): string[];
  HistoryEventKindNames(): string[];
  HistoryEventText(start: number, count: number): string[];
  HistorySettlementEndCauses(): string[];
  HistoryScars(): Arr;
  HistoryScarKinds(): string[];
  HistoryEvents(start: number, count: number): Arr;
  WaterBodies(): Arr;
  Ranges(): Arr;
  Deposits(): Arr;
  CitySites(max: number): Arr;
  CitySiteReasons(max: number): string[];
  NameStyles(): string[];
  NamePlaces(kinds: string[], seeds: number[], races: string[], cultures: string[], context: number[]): string[];
  RealmNaming(race: string, culture: string, form: string): string[];
}

let engine: Promise<EngineApi> | null = null;
let bootMs = 0;

async function boot(frameworkUrl: string): Promise<EngineApi> {
  const t0 = performance.now();
  const { dotnet } = await import(/* @vite-ignore */ frameworkUrl);
  const runtime = await dotnet.create();
  const asm = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  bootMs = performance.now() - t0;
  return asm.RoguelikeToolkit.World.MapEditor.Engine.WorldExports as EngineApi;
}

const api = (): Promise<EngineApi> => {
  if (!engine) throw new Error('world worker not initialised');
  return engine;
};

const styles = (e: EngineApi): NameStyle[] =>
  e.NameStyles().map((s) => { const [race, culture, label] = s.split('\t'); return { race, culture, label }; });

function names(e: EngineApi, reqs: NameRequest[]): string[] {
  if (reqs.length === 0) return [];
  return e.NamePlaces(reqs.map((r) => r.kind), reqs.map((r) => r.seed), reqs.map((r) => r.race),
    reqs.map((r) => r.culture), reqs.map((r) => r.context));
}

/** Which world the engine currently holds; recompute regenerates when the document asks for another. */
let loaded = '';
const planetOf = (p: GenerateParams) => ({ ...EARTH, ...p.planet });
const modelOf = (p: GenerateParams) => ({ ...CLIMATE_MODEL_DEFAULTS, ...p.model });
const historyOf = (p: GenerateParams): HistorySettings => ({ ...DEFAULT_HISTORY, ...p.history });
const worldKey = (p: GenerateParams): string => {
  const w = planetOf(p), m = modelOf(p);
  return `${JSON.stringify(historyOf(p))}|${p.seed}|${p.size}|${p.plates}|${w.obliquity}|${w.flux}|${w.rotation}|${m.climateRefresh}|${m.meridionalWind}|${m.windCellBlend}|${m.oceanHeat}|${m.oceanRain}|${m.aeolian}`;
};
const generateWorld = (e: EngineApi, p: GenerateParams): number => {
  const w = planetOf(p), m = modelOf(p), h = historyOf(p);
  e.ConfigureHistory(h.enabled, h.turns, h.years, h.maxPolities);
  e.ConfigureHistoryPeoples(h.roster.map((r) => r.folk), h.roster.map((r) => r.culture));
  e.ConfigureHistoryFounders(h.founders.map((f) => f.folk), h.founders.map((f) => f.culture), h.founders.map((f) => f.form),
    h.founders.map((f) => (f.lat === null || f.lon === null ? NaN : f.lat)), h.founders.map((f) => (f.lat === null || f.lon === null ? NaN : f.lon)));
  return e.Generate(p.seed, p.size, p.plates, w.obliquity, w.flux, w.rotation, m.climateRefresh, m.meridionalWind, m.windCellBlend, m.oceanHeat, m.oceanRain, m.aeolian);
};

async function generate(p: GenerateParams): Promise<WorldSnapshot> {
  const e = await api();
  const timings: Record<string, number> = { bootMs };
  const t = performance.now();
  loaded = ''; // Generate retires the old world first: if the build throws, nothing is loaded any more
  const tileCount = generateWorld(e, p);
  loaded = worldKey(p);
  timings.generateMs = performance.now() - t;
  return pull(e, p, tileCount, timings, { features: true, names: true });
}

async function recompute(p: RecomputeParams): Promise<WorldSnapshot> {
  const e = await api();
  const timings: Record<string, number> = { bootMs };
  let t = performance.now();
  if (loaded !== worldKey(p)) {
    loaded = ''; // as in generate: a failed build leaves no world, and the next call must build again
    generateWorld(e, p);
    loaded = worldKey(p);
  }
  const n = e.ApplyEdits(Array.from(p.edits.tiles), Array.from(p.edits.delta), Array.from(p.edits.pinHeight), Array.from(p.edits.meta), Array.from(p.edits.temperature), Array.from(p.edits.precipitation));
  timings.recomputeMs = performance.now() - t;
  // Names are the document's business on a recompute: surviving features keep theirs, and the
  // editor asks only for the ones that are new.
  return pull(e, p, n, timings, { features: !p.rastersOnly, names: false });
}

/** Event kind of a holy site's founding (`HistoryEventKind.HolyFound`). */
const HOLY_FOUND = 11;

/** Where polities founded holy sites: the placed `HolyFound` events of the log. */
function holySites(e: EngineApi): { tile: number; polity: number; turn: number }[] {
  const out: { tile: number; polity: number; turn: number }[] = [];
  for (let start = 0; ; start += 512) {
    const page = Int32Array.from(e.HistoryEvents(start, 512));
    const n = page[0];
    if (!n) break;
    // [n, (id, kind, turn, polity, tile, cause, importance, parts, (entity, role) * parts) * n]
    let o = 1;
    for (let i = 0; i < n; i++) {
      const kind = page[o + 1], turn = page[o + 2], polity = page[o + 3], tile = page[o + 4], parts = page[o + 7];
      if (kind === HOLY_FOUND && tile >= 0) out.push({ tile, polity, turn });
      o += 8 + parts * 2;
    }
  }
  return out;
}

/**
 * History's settlements and the names of its polities; none when the world has no history. Each polity renders its places
 * through the name style of its culture (the engine resolves that), which is added to `chosen` when the user left it out.
 */
function historyInput(e: EngineApi, seed: number, chosen: NameStyle[], all: NameStyle[]): HistoryInput {
  const pol = e.HistoryPolities(), keys = e.HistoryPolityKeys();
  const ids: number[] = [], folk: string[] = [], style: number[] = [];
  const indexIn = (list: NameStyle[], race: string, culture: string) => list.findIndex((s) => s.race === race && s.culture === culture);
  for (let i = 0; i < keys.length; i++) {
    const [f = '', , , , race = '', culture = ''] = keys[i].split('\t');
    ids.push(pol[i * 8]); folk.push(f);
    // Exact style among the chosen, else exact among all (added), else any of the folk's race, chosen first.
    let at = race ? indexIn(chosen, race, culture) : -1;
    // Humans have one shipped people, named through the shared fantasy table: a human_* tradition the user picked takes its place.
    if (race === 'human' && culture === 'fantasy_common') {
      const picked: number[] = [];
      chosen.forEach((st, k) => { if (st.race === 'human' && st.culture.startsWith('human_')) picked.push(k); });
      if (picked.length) at = picked[hash2(seed, 0x5017 + pol[i * 8]) % picked.length];
    }
    if (at < 0 && race) {
      const extra = indexIn(all, race, culture);
      if (extra >= 0) { chosen.push(all[extra]); at = chosen.length - 1; }
    }
    if (at < 0) at = polityStyleIndex(chosen, '', '', f, seed, pol[i * 8]) ?? -1;
    if (at < 0) {
      const extra = all.find((s) => s.race === f);
      if (extra) { chosen.push(extra); at = chosen.length - 1; }
    }
    style.push(at);
  }
  // A realm is named in its people's tongue, from the land its form would take a name from: "Kingdom of Eldershaw", "Frostmaw Horde".
  const forms = e.HistoryFormNames();
  const formOf = (i: number) => forms[pol[i * 8 + 1]] ?? '';
  // How each people names a realm of each form is name data (Core's tables), asked once per (people, form).
  const namingCache = new Map<string, RealmNaming>();
  const namingOf = (i: number): RealmNaming => {
    const st = chosen[style[i]] ?? { race: 'human', culture: 'fantasy_common' }, key = `${st.race}|${st.culture}|${formOf(i)}`;
    let n = namingCache.get(key);
    if (!n) namingCache.set(key, n = parseRealmNaming(e.RealmNaming(st.race, st.culture, formOf(i))));
    return n;
  };
  const reqs = ids.map((id, i) => {
    const st = chosen[style[i]] ?? { race: 'human', culture: 'fantasy_common' };
    return realmNameRequest(seed, id, namingOf(i), st.race, st.culture);
  });
  const base = uniqueNamesSync(reqs, (r) => names(e, r)).names;
  const realmNames = ids.map((id, i) => realmTitle(seed, id, namingOf(i), base[i]));
  return {
    settlements: Int32Array.from(e.HistorySettlements()), polityIds: ids, polityNames: realmNames, polityBases: base, polityFolk: folk, polityStyle: style,
    endCauses: e.HistorySettlementEndCauses(), scars: Int32Array.from(e.HistoryScars()), scarKinds: e.HistoryScarKinds(), holy: holySites(e),
  };
}

/** The run's polities, event log and territory, for the document to keep. Null when the world has no history. */
function historyRecord(e: EngineApi, input: HistoryInput, chosenCount: number): HistoryRecord | null {
  const ids = Array.from(input.polityIds ?? []);
  if (!ids.length) return null;
  const info = Int32Array.from(e.HistoryInfo()), pol = e.HistoryPolities(), keys = e.HistoryPolityKeys();
  const strings = e.HistoryStrings(), forms = e.HistoryFormNames(), kinds = e.HistoryEventKindNames();
  const polities: PolityRecord[] = ids.map((id, i) => {
    const [folk = '', culture = '', overlay = ''] = keys[i].split('\t'), o = i * 8;
    const cause = pol[o + 6];
    return {
      id, form: forms[pol[o + 1]] ?? '', capital: pol[o + 2], founded: pol[o + 3], ended: pol[o + 4], parent: pol[o + 5],
      endCause: cause >= 0 ? strings[cause] ?? '' : '', folk, culture, overlay, name: String(input.polityNames![i]),
      style: input.polityStyle![i] < chosenCount ? input.polityStyle![i] : -1,
    };
  });
  const events: EventRecord[] = [];
  for (let start = 0; ; start += 512) {
    const page = Int32Array.from(e.HistoryEvents(start, 512)), text = e.HistoryEventText(start, 512);
    const n = page[0];
    if (!n) break;
    let o = 1;
    for (let i = 0; i < n; i++) {
      const [plugin = '', ...tags] = (text[i] ?? '').split('\t');
      events.push({ id: page[o], kind: kinds[page[o + 1]] ?? 'Plugin', turn: page[o + 2], polity: page[o + 3], tile: page[o + 4], cause: page[o + 5], importance: page[o + 6], plugin, tags });
      o += 8 + page[o + 7] * 2;
    }
  }
  const recorded = info[0] === 1;
  return {
    turns: info[1], turnYears: info[2], polities, events,
    ownerBase: Int32Array.from(recorded ? e.HistoryOwnershipBase() : e.HistoryOwnershipAt(1 << 30)),
    changes: recorded ? Int32Array.from(e.HistoryTerritoryChanges()) : new Int32Array(0),
  };
}

function pull(e: EngineApi, p: GenerateParams, tileCount: number, timings: Record<string, number>, want: { features: boolean; names: boolean }): WorldSnapshot {
  let t = performance.now();
  const vectors = Float64Array.from(e.TileVectors());
  const faces = Int32Array.from(e.Faces());
  const heights = Float64Array.from(e.Heights());
  const biomes = Uint8Array.from(e.Biomes());
  const tilePlate = Uint32Array.from(e.Plates());
  const plateDrift = Float64Array.from(e.PlateDrift());
  const flow = Float64Array.from(e.Flow());
  const climate = Float64Array.from(e.Climate());
  const capacity = Uint8Array.from(e.LandCapacity());
  const seaIce = Uint8Array.from(e.SeaIce());
  const climateField = projectClimate(e.ClimateField(), vectors, tileCount);
  const catalogs = {
    rivers: Int32Array.from(e.Rivers()), waterBodies: Int32Array.from(e.WaterBodies()),
    ranges: Int32Array.from(e.Ranges()), deposits: Int32Array.from(e.Deposits()),
    citySites: Int32Array.from(e.CitySites(4000)), citySiteReasons: e.CitySiteReasons(4000),
  };
  const all = styles(e);
  const wanted = p.styles.length ? p.styles : DEFAULT_STYLES;
  let chosen = all.filter((s) => wanted.includes(styleKey(s)));
  if (!chosen.length) chosen = all.slice(0, 1);
  timings.pullMs = performance.now() - t;

  t = performance.now();
  const lakeDepth = Float32Array.from(e.LakeDepth());
  const rasters = rasterize({ vectors, faces, heights, biomes, lakeDepth }, MAP_W, MAP_H);
  timings.rasterMs = performance.now() - t;

  let features: WorldSnapshot['features'] = { features: [], rivers: [], roads: [] };
  let tileStyle = new Uint8Array(0);
  let history: HistoryRecord | null = null;
  let realmBases: string[] = [];
  if (want.features) {
    t = performance.now();
    const historyIn = historyInput(e, p.seed, chosen, all);
    realmBases = Array.from(historyIn.polityBases ?? []);
    history = historyRecord(e, historyIn, chosen.length);
    const adj = buildAdjacency(tileCount, faces);
    const polityStyle = new Map<number, number>();
    Array.from(historyIn.polityIds!).forEach((id, i) => { if (historyIn.polityStyle![i] >= 0) polityStyle.set(id, historyIn.polityStyle![i]); });
    tileStyle = tileNamingStyles({ seed: p.seed, n: tileCount, adj, isLand: (t) => heights[t] >= 0, owner: Int32Array.from(e.HistoryOwnershipAt(1 << 30)), polityStyle, styleCount: chosen.length });
    features = extractFeatures({ seed: p.seed, history: historyIn, tileStyle, vectors, faces, heights, biomes, flow, climate, tilePlate, ...catalogs, waterPixels: rasters.waterPixels, elevation: { data: rasters.elevation, width: MAP_W, height: MAP_H }, styles: chosen });
    const el = rasters.elevation;
    features.roads = buildRoads({
      segments: e.HistoryRoads(), vectors, seed: p.seed,
      wet: (x, y) => el[Math.min(MAP_H - 1, Math.max(0, Math.floor(y))) * MAP_W + (Math.floor(x) % MAP_W)] < 0,
    });
    timings.featuresMs = performance.now() - t;
  }

  if (want.names) {
    t = performance.now();
    const named = features.features.filter((f) => f.request);
    // No two places share a name, and no place takes a realm's name ("Ashmaw" beside the "Kingdom of Ashmaw");
    // a re-rolled place keeps the request that named it.
    const got = uniqueNamesSync(named.map((f) => f.request!), (r) => names(e, r), realmBases);
    named.forEach((f, i) => { f.name = got.names[i]; f.request = got.requests[i]; });
    timings.namesMs = performance.now() - t;
  }

  const tileXY = new Float32Array(tileCount * 2);
  for (let i = 0; i < tileCount; i++) {
    const q = vecToMap(vectors[i * 3], vectors[i * 3 + 1], vectors[i * 3 + 2]);
    tileXY[i * 2] = q.x; tileXY[i * 2 + 1] = q.y;
  }
  return { seed: p.seed, size: p.size, plates: p.plates, tileCount, rasters, tilePlate, tileStyle, history, plateDrift, tileXY, capacity, seaIce, climate: climateField, features, styles: chosen, timings };
}

const post = (msg: WorkerResponse, transfer: Transferable[] = []) => (self as unknown as DedicatedWorkerGlobalScope).postMessage(msg, transfer);

// addEventListener, not `self.onmessage =`: the .NET browser runtime treats an assigned onmessage
// in a worker as its own channel and dotnet.create() never resolves (verified in Chromium).
self.addEventListener('message', async (ev: MessageEvent<WorkerRequest>) => {
  const req = ev.data;
  try {
    switch (req.type) {
      case 'init':
        // A failed boot must not be cached: the next init (a fresh worker, or a retry here) boots again.
        engine ??= boot(req.frameworkUrl).catch((err: unknown) => { engine = null; throw err; });
        await engine;
        post({ id: req.id, ok: true, type: 'init', bootMs });
        break;
      case 'generate': {
        const world = await generate(req.params);
        post({ id: req.id, ok: true, type: 'generate', world }, snapshotTransfer(world));
        break;
      }
      case 'recompute': {
        const world = await recompute(req.params);
        post({ id: req.id, ok: true, type: 'recompute', world }, snapshotTransfer(world));
        break;
      }
      case 'names':
        post({ id: req.id, ok: true, type: 'names', names: names(await api(), req.requests) });
        break;
      case 'styles':
        post({ id: req.id, ok: true, type: 'styles', styles: styles(await api()) });
        break;
      case 'peoples': {
        const e = await api();
        const peoples = e.HistoryPeoples().map((line) => {
          const [folk, culture, folkName, cultureName, subsistence, affinity] = line.split('\t');
          return { folk, culture, folkName, cultureName, subsistence, affinity: (affinity ?? '').split(',').filter(Boolean).map((a) => { const [form, w] = a.split(':'); return { form, weight: Number(w) }; }) };
        });
        post({ id: req.id, ok: true, type: 'peoples', peoples, forms: e.HistoryFormNames() });
        break;
      }
    }
  } catch (err) {
    post({ id: req.id, ok: false, error: String((err as Error)?.stack ?? err) });
  }
});
