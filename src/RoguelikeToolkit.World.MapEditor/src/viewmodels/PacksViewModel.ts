import type { Editor } from '../engine/Editor';
import { USER_PACK_ID } from '../engine/content/PackRegistry';
import { ViewModel, type FileService } from './ViewModel';

export interface PackRow {
  id: string;
  name: string;
  version: string;
  author: string;
  description: string;
  enabled: boolean;
  removable: boolean;
  summary: string;
}

export interface PacksState { packs: PackRow[]; error: string | null; }

/** Content pack manager: import JSON packs, toggle, remove, export the user overlay. */
export class PacksViewModel extends ViewModel<PacksState> {
  constructor(private readonly editor: Editor, private readonly files: FileService) {
    super({ packs: [], error: null });
    this.track(editor.events.on('content', () => this.sync()));
    this.sync();
  }

  private sync(): void {
    this.set({
      packs: this.editor.packs.packs.map(({ pack: p, enabled }, i) => ({
        id: p.id, name: p.name, version: p.version, author: p.author ?? '', description: p.description ?? '', enabled,
        removable: i > 0,
        summary: [
          p.themes?.length && `${p.themes.length} themes`, p.biomes?.length && `${p.biomes.length} biomes`,
          p.assets?.length && `${p.assets.length} assets`, p.rules?.length && `${p.rules.length} rules`,
          p.fonts && Object.keys(p.fonts).length && `${Object.keys(p.fonts).length} fonts`,
        ].filter(Boolean).join(' · '),
      })),
    });
  }

  importFile = async (): Promise<void> => {
    const f = await this.files.pick('.json,application/json');
    if (!f) return;
    try {
      // Relative URLs inside a local file cannot resolve; the page URL is the honest base.
      this.editor.addPack(JSON.parse(await f.text()), location.href);
      this.set({ error: null });
    } catch (e) {
      this.set({ error: e instanceof Error ? e.message : String(e) });
    }
  };

  importUrl = async (url: string): Promise<void> => {
    try {
      const abs = new URL(url, location.href).href;
      const r = await fetch(abs);
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      this.editor.addPack(await r.json(), abs);
      this.set({ error: null });
    } catch (e) {
      this.set({ error: `${url}: ${e instanceof Error ? e.message : String(e)}` });
    }
  };

  toggle = (id: string): void => {
    const p = this.state.packs.find((x) => x.id === id);
    if (p) this.editor.packs.setEnabled(id, !p.enabled);
  };

  remove = (id: string): void => this.editor.packs.remove(id);

  exportUser = (): void => {
    const p = this.editor.packs.userPack();
    if (p) this.files.save(new Blob([JSON.stringify(p, null, 2)], { type: 'application/json' }), `${USER_PACK_ID}-pack.json`);
  };

  dismissError = (): void => this.set({ error: null });
}
