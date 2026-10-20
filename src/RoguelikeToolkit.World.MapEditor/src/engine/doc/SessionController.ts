import type { MapDocument } from './MapDocument';
import type { SessionStore } from './SessionStore';
import { loadDocument, saveDocument } from './serialize';

export interface SessionHost {
  /** The document being edited right now. */
  current(): MapDocument;
  /** Makes a restored document the current one. */
  open(doc: MapDocument): void;
  status(message: string): void;
}

const DEBOUNCE_MS = 4000;

/** Keeps the working map in a SessionStore so the next visit can pick up where this one ended. */
export class SessionController {
  private timer = 0;
  private dirty = false;

  constructor(private readonly store: SessionStore | undefined, private readonly host: SessionHost) {
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') void this.flush(); });
    }
  }

  /** Whether a map from an earlier visit is stored. */
  async has(): Promise<boolean> { return !!(await this.store?.load()); }

  /** Re-opens the map stored by the last visit; false when there is none (or it cannot be read). */
  async restore(): Promise<boolean> {
    const blob = await this.store?.load();
    if (!blob) return false;
    try {
      const doc = await loadDocument(blob);
      this.host.open(doc);
      this.host.status(`Restored "${doc.settings.title}" from your last visit`);
      return true;
    } catch {
      await this.store?.clear(); // unreadable (older format, corrupt): do not trip over it again
      return false;
    }
  }

  /** Forgets the stored map. */
  forget(): Promise<void> {
    clearTimeout(this.timer);
    this.dirty = false;
    return this.store?.clear() ?? Promise.resolve();
  }

  /** The working map changed: write it soon. */
  changed(): void {
    if (!this.store) return;
    this.dirty = true;
    clearTimeout(this.timer);
    this.timer = window.setTimeout(() => void this.flush(), DEBOUNCE_MS);
  }

  /** Writes the working map now, if it changed since the last write. */
  async flush(): Promise<void> {
    if (!this.store || !this.dirty) return;
    clearTimeout(this.timer);
    this.dirty = false;
    try { await this.store.save(await saveDocument(this.host.current())); } catch { this.dirty = true; }
  }
}
