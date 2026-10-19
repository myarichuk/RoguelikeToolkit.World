/** Where the editor keeps the map it was last working on, so a reload picks up where it left off. */
export interface SessionStore {
  load(): Promise<Blob | null>;
  save(map: Blob): Promise<void>;
  clear(): Promise<void>;
}

const DB = 'wme-session';
const STORE = 'session';
const KEY = 'current';

/** IndexedDB-backed store. Every failure (private window, blocked storage, quota) degrades to "nothing stored". */
export class IndexedDbSessionStore implements SessionStore {
  private db: Promise<IDBDatabase | null> | null = null;

  private open(): Promise<IDBDatabase | null> {
    this.db ??= new Promise((resolve) => {
      try {
        const req = indexedDB.open(DB, 1);
        req.onupgradeneeded = () => req.result.createObjectStore(STORE);
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => resolve(null);
        req.onblocked = () => resolve(null);
      } catch { resolve(null); }
    });
    return this.db;
  }

  private async run<T>(mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T | null> {
    const db = await this.open();
    if (!db) return null;
    return new Promise((resolve) => {
      try {
        const tx = db.transaction(STORE, mode);
        const req = fn(tx.objectStore(STORE));
        tx.oncomplete = () => resolve(req.result ?? null);
        tx.onerror = tx.onabort = () => resolve(null);
      } catch { resolve(null); }
    });
  }

  async load(): Promise<Blob | null> {
    const bytes = await this.run<ArrayBuffer>('readonly', (s) => s.get(KEY));
    return bytes ? new Blob([bytes], { type: 'application/x-wmap' }) : null;
  }

  async save(map: Blob): Promise<void> {
    // An ArrayBuffer, not the Blob: older Safari cannot store Blobs in IndexedDB.
    const bytes = await map.arrayBuffer();
    await this.run('readwrite', (s) => s.put(bytes, KEY));
  }

  async clear(): Promise<void> { await this.run('readwrite', (s) => s.delete(KEY)); }
}
