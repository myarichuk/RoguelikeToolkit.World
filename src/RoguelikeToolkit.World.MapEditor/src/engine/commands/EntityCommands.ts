import type { EntityStore } from '../doc/EntityStore';
import type { Entity } from '../doc/types';
import type { Command } from './Command';

export class AddEntities<T extends Entity> implements Command {
  constructor(readonly label: string, private readonly store: EntityStore<T>, private readonly items: T[]) {}
  do(): void { this.store.add(this.items); }
  undo(): void { this.store.remove(this.items.map((i) => i.id)); }
}

export class RemoveEntities<T extends Entity> implements Command {
  private removed: T[] = [];
  constructor(readonly label: string, private readonly store: EntityStore<T>, private readonly ids: string[]) {}
  do(): void { this.removed = this.store.remove(this.ids); }
  undo(): void { this.store.add(this.removed); }
}

/**
 * Replaces entities with new values. `key` enables coalescing: consecutive updates with the same
 * key (one drag, one slider scrub) collapse into a single undo step.
 */
export class UpdateEntities<T extends Entity> implements Command {
  constructor(
    readonly label: string,
    private readonly store: EntityStore<T>,
    private readonly before: T[],
    private after: T[],
    private readonly key?: string,
  ) {}
  do(): void { this.store.update(this.after); }
  undo(): void { this.store.update(this.before); }
  merge(next: Command): boolean {
    if (!(next instanceof UpdateEntities) || !this.key || next.key !== this.key || next.store !== this.store) return false;
    this.after = next.after as T[];
    return true;
  }
}

/** Generic property change on a settings-like object, coalescing by key. */
export class SetValue<V> implements Command {
  constructor(readonly label: string, private readonly apply: (v: V) => void, private readonly before: V, private after: V, private readonly key?: string) {}
  do(): void { this.apply(this.after); }
  undo(): void { this.apply(this.before); }
  merge(next: Command): boolean {
    if (!(next instanceof SetValue) || !this.key || next.key !== this.key) return false;
    this.after = next.after as V;
    return true;
  }
}
