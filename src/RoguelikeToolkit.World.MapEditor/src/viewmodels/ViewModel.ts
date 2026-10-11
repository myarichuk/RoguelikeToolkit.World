// MVVM: a view model owns presentation state derived from the Editor and exposes commands.
// Views (React components) render `state` and call commands; they never touch the engine.
// State objects are immutable snapshots, so `useSyncExternalStore` can compare by reference.

export abstract class ViewModel<S extends object> {
  private readonly listeners = new Set<() => void>();
  private readonly disposers: (() => void)[] = [];
  private _state: S;

  protected constructor(initial: S) { this._state = initial; }

  get state(): S { return this._state; }

  readonly subscribe = (fn: () => void): (() => void) => {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  };

  readonly getSnapshot = (): S => this._state;

  protected set(patch: Partial<S>): void {
    let changed = false;
    for (const k in patch) if (patch[k] !== this._state[k]) { changed = true; break; }
    if (!changed) return;
    this._state = { ...this._state, ...patch };
    for (const fn of [...this.listeners]) fn();
  }

  /** Registers an unsubscribe to run on dispose. */
  protected track(off: () => void): void { this.disposers.push(off); }

  dispose(): void {
    this.disposers.splice(0).forEach((f) => f());
    this.listeners.clear();
  }
}

/** Platform services view models need but must not implement (keeps them DOM-free and testable). */
export interface FileService {
  save(blob: Blob, filename: string): void;
  pick(accept: string): Promise<File | null>;
}
