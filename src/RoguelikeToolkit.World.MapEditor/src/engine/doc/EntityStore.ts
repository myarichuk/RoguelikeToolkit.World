import { Emitter } from '../core/Emitter';
import type { Entity } from './types';

export interface EntityChange<T> { added: T[]; removed: T[]; updated: T[]; }

/**
 * An observable, ordered collection of entities (the document's "repository" per kind). Order is
 * insertion order and is what renderers paint in (stamps re-sort by y themselves). Mutations go
 * through commands (see commands/), which call these methods; listeners re-derive GPU buffers.
 */
export class EntityStore<T extends Entity> {
  private readonly items = new Map<string, T>();
  readonly events = new Emitter<{ change: EntityChange<T> }>();
  /** Bumps on every mutation; cheap cache key for derived data. */
  version = 0;

  get size(): number { return this.items.size; }
  get(id: string): T | undefined { return this.items.get(id); }
  has(id: string): boolean { return this.items.has(id); }
  all(): IterableIterator<T> { return this.items.values(); }
  toArray(): T[] { return [...this.items.values()]; }

  add(items: readonly T[]): void {
    if (!items.length) return;
    for (const it of items) this.items.set(it.id, it);
    this.changed({ added: [...items], removed: [], updated: [] });
  }

  remove(ids: readonly string[]): T[] {
    const removed: T[] = [];
    for (const id of ids) {
      const it = this.items.get(id);
      if (it) { this.items.delete(id); removed.push(it); }
    }
    if (removed.length) this.changed({ added: [], removed, updated: [] });
    return removed;
  }

  /** Replaces entities by id (entities are treated as immutable values). */
  update(items: readonly T[]): void {
    const updated = items.filter((it) => this.items.has(it.id));
    for (const it of updated) this.items.set(it.id, it);
    if (updated.length) this.changed({ added: [], removed: [], updated });
  }

  /** Replaces everything (load / regenerate). */
  reset(items: readonly T[]): void {
    const removed = this.toArray();
    this.items.clear();
    for (const it of items) this.items.set(it.id, it);
    this.changed({ added: [...items], removed, updated: [] });
  }

  private changed(c: EntityChange<T>): void {
    this.version++;
    this.events.emit('change', c);
  }
}
