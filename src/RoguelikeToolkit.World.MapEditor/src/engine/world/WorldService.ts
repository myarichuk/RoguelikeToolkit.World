// Main-thread proxy for the world worker (Proxy pattern): promise-based RPC over postMessage.
// Callers never see the worker, the .NET runtime, or message ids.
import type { GenerateParams, HistoryFeature, NameRequest, PeopleOption, RecomputeParams, NameStyle, WorkerRequest, WorkerResponse, WorldSnapshot } from './protocol';

type Ok = Extract<WorkerResponse, { ok: true }>;
type Pending = { resolve: (r: Ok) => void; reject: (e: Error) => void };
type Body<T> = T extends unknown ? Omit<T, 'id'> : never;
type Reply<K extends WorkerRequest['type']> = Extract<Ok, { type: K }>;

/** How long the engine may take to boot before the editor gives up and says so (a stalled download otherwise looks like a hang). */
export const BOOT_TIMEOUT_MS = 90_000;

export class WorldService {
  private worker!: Worker;
  private readonly pending = new Map<number, Pending>();
  private nextId = 1;
  /** Resolves with the boot time (ms) once the engine is up; replaced by `restart`. */
  ready!: Promise<number>;

  constructor(private readonly frameworkUrl: string, private readonly bootTimeoutMs = BOOT_TIMEOUT_MS) {
    this.spawn();
  }

  /** Starts a fresh worker and boots the engine in it. */
  private spawn(): void {
    const worker = new Worker(new URL('../../workers/world.worker.ts', import.meta.url), { type: 'module' });
    this.worker = worker;
    worker.onmessage = (e: MessageEvent<WorkerResponse>) => {
      const p = this.pending.get(e.data.id);
      if (!p) return;
      this.pending.delete(e.data.id);
      if (e.data.ok) p.resolve(e.data); else p.reject(new Error(e.data.error));
    };
    // A crashed worker (out of memory, a failed download) takes its in-flight calls with it; the next call starts a new one.
    worker.onerror = (e) => this.fail(worker, new Error(e.message || 'The world engine crashed (it may have run out of memory). Try a lower detail level.'));
    this.ready = this.boot(worker);
  }

  private boot(worker: Worker): Promise<number> {
    const boot = this.call<'init'>({ type: 'init', frameworkUrl: this.frameworkUrl }, worker).then((r) => r.bootMs);
    // The watchdog fails the worker, which rejects the init call above with a message the editor can show.
    const timer = setTimeout(() => this.fail(worker, new Error(`The world engine did not start within ${Math.round(this.bootTimeoutMs / 1000)} s. Check the connection and reload; if it keeps happening the host may be throttling the download.`)), this.bootTimeoutMs);
    const stop = () => clearTimeout(timer);
    void boot.then(stop, stop);
    // A boot the engine itself rejected (a failed download, a runtime error) marks the worker dead like a crash
    // would, so the next call spawns a fresh one instead of awaiting this rejection forever.
    void boot.catch((err: unknown) => this.fail(worker, err instanceof Error ? err : new Error(String(err))));
    return boot;
  }

  /** Rejects what the worker had in flight, drops it, and leaves the next call to start a new one. */
  private fail(worker: Worker, err: Error): void {
    if (worker !== this.worker) return;
    for (const p of this.pending.values()) p.reject(err);
    this.pending.clear();
    worker.terminate();
    this.dead = true;
  }

  private dead = false;

  /** Gives up on the running work (a generation that is taking too long) and starts a fresh engine. */
  restart(reason = 'Cancelled'): void {
    this.fail(this.worker, new Error(reason));
    this.revive();
  }

  private revive(): void {
    if (!this.dead) return;
    this.dead = false;
    this.spawn();
  }

  private call<K extends WorkerRequest['type']>(req: Body<Extract<WorkerRequest, { type: K }>>, worker: Worker = this.worker): Promise<Reply<K>> {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (r: Ok) => void, reject });
      worker.postMessage({ ...req, id } as WorkerRequest);
    });
  }

  async generate(params: GenerateParams): Promise<WorldSnapshot> {
    await this.alive();
    return (await this.call<'generate'>({ type: 'generate', params })).world;
  }

  /** Re-derives the world from the hand-edit table (see Core's World.Recompute). Regenerates first if the engine holds another world. */
  async recompute(params: RecomputeParams): Promise<WorldSnapshot> {
    await this.alive();
    return (await this.call<'recompute'>({ type: 'recompute', params })).world;
  }

  async names(requests: NameRequest[]): Promise<string[]> {
    await this.alive();
    return (await this.call<'names'>({ type: 'names', requests })).names;
  }

  async styles(): Promise<NameStyle[]> {
    await this.alive();
    return (await this.call<'styles'>({ type: 'styles' })).styles;
  }

  /** The peoples a world can be founded by, the polity form names (indexed by the value founders pin) and the history expansion features. */
  async peoples(): Promise<{ peoples: PeopleOption[]; forms: string[]; features: HistoryFeature[] }> {
    await this.alive();
    const r = await this.call<'peoples'>({ type: 'peoples' });
    return { peoples: r.peoples, forms: r.forms, features: r.features ?? [] };
  }

  /** The engine, restarted first when the last one died. */
  private async alive(): Promise<void> {
    if (this.dead) this.revive();
    await this.ready;
  }

  dispose(): void { this.worker.terminate(); }
}
