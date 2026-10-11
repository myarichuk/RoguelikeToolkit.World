/// <reference lib="webworker" />
// The world service: RoguelikeToolkit.World.Core (browser-wasm) off the main thread. It owns the
// .NET runtime; the editor only ever sees plain data (protocol.ts). Rasterization and feature
// extraction run here too, in V8 — measurably faster than the Mono interpreter for per-pixel loops.
import { MAP_H, MAP_W, vecToMap } from '../engine/core/geo';
import { extractFeatures } from '../engine/world/features';
import {
  DEFAULT_STYLES, snapshotTransfer, styleKey,
  type GenerateParams, type NameRequest, type NameStyle, type WorkerRequest, type WorkerResponse, type WorldSnapshot,
} from '../engine/world/protocol';
import { rasterize } from '../engine/world/rasterize';

type Arr = ArrayLike<number>;

/** The [JSExport] surface of RoguelikeToolkit.World.MapEditor.Engine.WorldExports. */
interface EngineApi {
  Generate(seed: number, size: number, plates: number): number;
  TileVectors(): Arr;
  Faces(): Arr;
  Heights(): Arr;
  Biomes(): Arr;
  Plates(): Arr;
  PlateDrift(): Arr;
  Flow(): Arr;
  Climate(): Arr;
  Rivers(): Arr;
  WaterBodies(): Arr;
  Ranges(): Arr;
  Deposits(): Arr;
  CitySites(max: number): Arr;
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

async function generate(p: GenerateParams): Promise<WorldSnapshot> {
  const e = await api();
  const timings: Record<string, number> = { bootMs };
  let t = performance.now();
  const tileCount = e.Generate(p.seed, p.size, p.plates);
  timings.generateMs = performance.now() - t;

  t = performance.now();
  const vectors = Float64Array.from(e.TileVectors());
  const faces = Int32Array.from(e.Faces());
  const heights = Float64Array.from(e.Heights());
  const biomes = Uint8Array.from(e.Biomes());
  const tilePlate = Uint32Array.from(e.Plates());
  const plateDrift = Float64Array.from(e.PlateDrift());
  const flow = Float64Array.from(e.Flow());
  const climate = Float64Array.from(e.Climate());
  const catalogs = {
    rivers: Int32Array.from(e.Rivers()), waterBodies: Int32Array.from(e.WaterBodies()),
    ranges: Int32Array.from(e.Ranges()), deposits: Int32Array.from(e.Deposits()),
    citySites: Int32Array.from(e.CitySites(4000)),
  };
  const all = styles(e);
  const wanted = p.styles.length ? p.styles : DEFAULT_STYLES;
  let chosen = all.filter((s) => wanted.includes(styleKey(s)));
  if (!chosen.length) chosen = all.slice(0, 1);
  timings.pullMs = performance.now() - t;

  t = performance.now();
  const rasters = rasterize({ vectors, faces, heights, biomes }, MAP_W, MAP_H);
  timings.rasterMs = performance.now() - t;

  t = performance.now();
  const features = extractFeatures({ seed: p.seed, vectors, faces, heights, biomes, flow, climate, tilePlate, ...catalogs, styles: chosen });
  timings.featuresMs = performance.now() - t;

  t = performance.now();
  const named = features.features.filter((f) => f.request);
  const placeNames = names(e, named.map((f) => f.request!));
  named.forEach((f, i) => { f.name = placeNames[i]; });
  timings.namesMs = performance.now() - t;

  const tileXY = new Float32Array(tileCount * 2);
  for (let i = 0; i < tileCount; i++) {
    const q = vecToMap(vectors[i * 3], vectors[i * 3 + 1], vectors[i * 3 + 2]);
    tileXY[i * 2] = q.x; tileXY[i * 2 + 1] = q.y;
  }
  return { seed: p.seed, size: p.size, plates: p.plates, tileCount, rasters, tilePlate, plateDrift, tileXY, features, styles: chosen, timings };
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
      case 'names':
        post({ id: req.id, ok: true, type: 'names', names: names(await api(), req.requests) });
        break;
      case 'styles':
        post({ id: req.id, ok: true, type: 'styles', styles: styles(await api()) });
        break;
    }
  } catch (err) {
    post({ id: req.id, ok: false, error: String((err as Error)?.stack ?? err) });
  }
});
