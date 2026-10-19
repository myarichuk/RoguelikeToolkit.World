import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WorldService } from './WorldService';

/** A worker that answers `init` after a delay (or never) and records what it was asked. */
class FakeWorker {
  static all: FakeWorker[] = [];
  static bootAfterMs: number | null = 10;
  onmessage: ((e: MessageEvent) => void) | null = null;
  onerror: ((e: { message: string }) => void) | null = null;
  terminated = false;
  constructor() { FakeWorker.all.push(this); }
  postMessage(req: { id: number; type: string }) {
    if (req.type === 'init') {
      if (FakeWorker.bootAfterMs !== null) setTimeout(() => this.onmessage?.({ data: { id: req.id, ok: true, type: 'init', bootMs: 7 } } as MessageEvent), FakeWorker.bootAfterMs);
    } else if (req.type === 'styles') {
      setTimeout(() => this.onmessage?.({ data: { id: req.id, ok: true, type: 'styles', styles: [] } } as MessageEvent), 1);
    }
  }
  terminate() { this.terminated = true; }
}

describe('WorldService', () => {
  beforeEach(() => { FakeWorker.all = []; FakeWorker.bootAfterMs = 10; vi.stubGlobal('Worker', FakeWorker); vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

  it('boots and answers calls', async () => {
    const svc = new WorldService('x', 1000);
    const styles = svc.styles();
    await vi.advanceTimersByTimeAsync(50);
    await expect(svc.ready).resolves.toBe(7);
    await expect(styles).resolves.toEqual([]);
  });

  it('says so when the engine never starts, instead of waiting forever', async () => {
    FakeWorker.bootAfterMs = null;
    const svc = new WorldService('x', 1000);
    const failed = expect(svc.ready).rejects.toThrow(/did not start within 1 s/);
    await vi.advanceTimersByTimeAsync(1500);
    await failed;
    expect(FakeWorker.all[0].terminated).toBe(true);
  });

  it('starts a fresh engine for the next call after a crash', async () => {
    const svc = new WorldService('x', 1000);
    await vi.advanceTimersByTimeAsync(50);
    await svc.ready;
    FakeWorker.all[0].onerror?.({ message: 'out of memory' });
    expect(FakeWorker.all[0].terminated).toBe(true);
    const styles = svc.styles();
    await vi.advanceTimersByTimeAsync(50);
    await expect(styles).resolves.toEqual([]);
    expect(FakeWorker.all).toHaveLength(2);
  });

  it('fails what was in flight when the worker crashes, with the reason', async () => {
    const svc = new WorldService('x', 1000);
    await vi.advanceTimersByTimeAsync(50);
    const styles = svc.styles();
    const failed = expect(styles).rejects.toThrow(/out of memory/);
    await vi.advanceTimersByTimeAsync(0);
    FakeWorker.all[0].onerror?.({ message: 'out of memory' });
    await failed;
  });

  it('restart drops the running work and boots a new engine', async () => {
    const svc = new WorldService('x', 1000);
    await vi.advanceTimersByTimeAsync(50);
    svc.restart('Cancelled by the user');
    expect(FakeWorker.all).toHaveLength(2);
    expect(FakeWorker.all[0].terminated).toBe(true);
    await vi.advanceTimersByTimeAsync(50);
    await expect(svc.ready).resolves.toBe(7);
  });
});
