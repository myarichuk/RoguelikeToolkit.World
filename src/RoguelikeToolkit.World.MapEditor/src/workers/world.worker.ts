/// <reference lib="webworker" />
// The world service: RoguelikeToolkit.World.Core (browser-wasm) off the main thread. It owns the
// .NET runtime; the editor only ever sees plain data (protocol.ts). Rasterization and feature
// extraction run here too, in V8 — measurably faster than the Mono interpreter for per-pixel loops.
import { MAP_H, MAP_W, vecToMap } from '../engine/core/geo';
import { projectClimate } from '../engine/world/physics/climate';
import { extractFeatures } from '../engine/world/features';
import { buildRoads } from '../engine/world/history/roads';
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
  const tileCount = generateWorld(e, p);
  loaded = worldKey(p);
  timings.generateMs = performance.now() - t;
  return pull(e, p, tileCount, timings, { features: true, names: true });
}

async function recompute(p: RecomputeParams): Promise<WorldSnapshot> {
  const e = await api();
  const timings: Record<string, number> = { bootMs };
  let t = performance.now();
  const tileCount = loaded === worldKey(p) ? -1 : generateWorld(e, p);
  if (tileCount >= 0) loaded = worldKey(p);
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

/** History's settlements and the names of its polities; none when the world has no history. */
function historyInput(e: EngineApi): HistoryInput {
  const pol = e.HistoryPolities(), keys = e.HistoryPolityKeys();
  const ids: number[] = [], names: string[] = [];
  for (let i = 0; i < keys.length; i++) { ids.push(pol[i * 8]); names.push(keys[i].split('\t')[3] ?? ''); }
  const folk = keys.map((k) => k.split('\t')[0] ?? '');
  return {
    settlements: Int32Array.from(e.HistorySettlements()), polityIds: ids, polityNames: names, polityFolk: folk,
    endCauses: e.HistorySettlementEndCauses(), scars: Int32Array.from(e.HistoryScars()), scarKinds: e.HistoryScarKinds(), holy: holySites(e),
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
  if (want.features) {
    t = performance.now();
    features = extractFeatures({ seed: p.seed, history: historyInput(e), vectors, faces, heights, biomes, flow, climate, tilePlate, ...catalogs, waterPixels: rasters.waterPixels, elevation: { data: rasters.elevation, width: MAP_W, height: MAP_H }, styles: chosen });
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
    // No two places share a name; a re-rolled place keeps the request that named it.
    const got = uniqueNamesSync(named.map((f) => f.request!), (r) => names(e, r));
    named.forEach((f, i) => { f.name = got.names[i]; f.request = got.requests[i]; });
    timings.namesMs = performance.now() - t;
  }

  const tileXY = new Float32Array(tileCount * 2);
  for (let i = 0; i < tileCount; i++) {
    const q = vecToMap(vectors[i * 3], vectors[i * 3 + 1], vectors[i * 3 + 2]);
    tileXY[i * 2] = q.x; tileXY[i * 2 + 1] = q.y;
  }
  return { seed: p.seed, size: p.size, plates: p.plates, tileCount, rasters, tilePlate, plateDrift, tileXY, capacity, seaIce, climate: climateField, features, styles: chosen, timings };
}

const post = (msg: WorkerResponse, transfer: Transferable[] = []) => (self as unknown as DedicatedWorkerGlobalScope).postMessage(msg, transfer);

// addEventListener, not `self.onmessage =`: the .NET browser runtime treats an assigned onmessage
// in a worker as its own channel and dotnet.create() never resolves (verified in Chromium).
self.addEventListener('message', async (ev: MessageEvent<WorkerRequest>) => {
  const req = ev.data;
  try {
    switch (req.type) {
      case 'init':
        engine ??= boot(req.frameworkUrl);
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
