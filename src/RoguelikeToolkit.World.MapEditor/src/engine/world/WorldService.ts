// Main-thread proxy for the world worker (Proxy pattern): promise-based RPC over postMessage.
// Callers never see the worker, the .NET runtime, or message ids.
import type { GenerateParams, NameRequest, NameStyle, WorkerRequest, WorkerResponse, WorldSnapshot } from './protocol';

type Ok = Extract<WorkerResponse, { ok: true }>;
type Pending = { resolve: (r: Ok) => void; reject: (e: Error) => void };
type Body<T> = T extends unknown ? Omit<T, 'id'> : never;
type Reply<K extends WorkerRequest['type']> = Extract<Ok, { type: K }>;

export class WorldService {
  private readonly worker: Worker;
  private readonly pending = new Map<number, Pending>();
  private nextId = 1;
  readonly ready: Promise<number>;

  constructor(frameworkUrl: string) {
    this.worker = new Worker(new URL('../../workers/world.worker.ts', import.meta.url), { type: 'module' });
    this.worker.onmessage = (e: MessageEvent<WorkerResponse>) => {
      const p = this.pending.get(e.data.id);
      if (!p) return;
      this.pending.delete(e.data.id);
      if (e.data.ok) p.resolve(e.data); else p.reject(new Error(e.data.error));
    };
    this.worker.onerror = (e) => {
      const err = new Error(e.message || 'world worker crashed');
      for (const p of this.pending.values()) p.reject(err);
      this.pending.clear();
    };
    this.ready = this.call<'init'>({ type: 'init', frameworkUrl }).then((r) => r.bootMs);
  }

  private call<K extends WorkerRequest['type']>(req: Body<Extract<WorkerRequest, { type: K }>>): Promise<Reply<K>> {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (r: Ok) => void, reject });
      this.worker.postMessage({ ...req, id } as WorkerRequest);
    });
  }

  async generate(params: GenerateParams): Promise<WorldSnapshot> {
    await this.ready;
    return (await this.call<'generate'>({ type: 'generate', params })).world;
  }

  async names(requests: NameRequest[]): Promise<string[]> {
    await this.ready;
    return (await this.call<'names'>({ type: 'names', requests })).names;
  }

  async styles(): Promise<NameStyle[]> {
    await this.ready;
    return (await this.call<'styles'>({ type: 'styles' })).styles;
  }

  dispose(): void { this.worker.terminate(); }
}
