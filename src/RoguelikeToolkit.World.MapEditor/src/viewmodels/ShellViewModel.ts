import { CAPACITY_KINDS, capacityOf } from '../engine/world/capacity';
import { landUseName } from '../engine/world/landuse';
import type { Editor } from '../engine/Editor';
import { kmPerMu, mapToLonLatDeg, wrapX } from '../engine/core/geo';
import type { UndoState } from '../engine/commands/UndoStack';
import { svgOptionsFromView } from '../engine/export/SvgExporter';
import { ViewModel, type FileService } from './ViewModel';

export interface CursorInfo { lon: number; lat: number; elevation: number; biome: string; feature: string; kmPerPx: number; }

export interface ShellState {
  title: string;
  busy: string | null;
  status: string;
  error: string | null;
  undo: UndoState;
  themeId: string;
  themes: { id: string; name: string; edited: boolean }[];
  /** CSS custom properties for the chrome, from the active theme. */
  chrome: Record<string, string>;
  frame: string;
  cursor: CursorInfo | null;
  timings: Record<string, number>;
}

/** App-level state and commands: file menu, undo/redo, theme choice, status bar. */
export class ShellViewModel extends ViewModel<ShellState> {
  private pending: { x: number; y: number } | null = null;
  private raf = 0;

  constructor(private readonly editor: Editor, private readonly files: FileService) {
    super({
      title: editor.doc.settings.title, busy: null, status: 'Starting the world engine…', error: null, undo: editor.undo.state,
      themeId: editor.themeId, themes: [], chrome: {}, frame: '', cursor: null, timings: {},
    });
    const e = editor.events;
    this.track(e.on('busy', (busy) => this.set({ busy })));
    this.track(e.on('status', (status) => this.set({ status })));
    this.track(e.on('error', (error) => this.set({ error })));
    this.track(e.on('timings', (t) => this.set({ timings: { ...this.state.timings, ...t } })));
    this.track(e.on('doc', (doc) => this.set({ title: doc.settings.title })));
    this.track(e.on('theme', (t) => this.set({ themeId: t.id, chrome: t.ui ?? {} })));
    this.track(e.on('content', () => this.syncThemes()));
    this.track(e.on('frame', (s) => this.set({ frame: `${s.frameMs.toFixed(1)} ms/frame · terrain ${s.terrainMs.toFixed(1)} ms · ${s.stamps} stamps` })));
    this.track(e.on('pointer', (p) => this.onPointer(p)));
    this.track(e.on('shortcut', (id) => void (id === 'save' ? this.save() : this.open())));
    this.track(editor.undo.events.on('change', (undo) => this.set({ undo })));
    this.syncThemes();
    void editor.world.ready.then((ms) => this.set({ status: `World engine ready (${Math.round(ms)} ms boot)` }), () => {});
  }

  private syncThemes(): void {
    const p = this.editor.packs;
    this.set({ themes: p.themes().map((t) => ({ id: t.id, name: t.name, edited: p.isEdited(t.id) })) });
  }

  private onPointer(p: { x: number; y: number } | null): void {
    this.pending = p;
    if (!p) { this.set({ cursor: null }); return; }
    if (this.raf) return;
    this.raf = requestAnimationFrame(() => {
      this.raf = 0;
      const q = this.pending;
      if (!q) return;
      const doc = this.editor.doc, ll = mapToLonLatDeg(wrapX(q.x), q.y);
      if (q.y < 0 || q.y >= doc.height) { this.set({ cursor: null }); return; }
      const biomes = this.editor.packs.biomes();
      const area = doc.featuresAt(q.x, q.y).filter((f) => f.name).sort((a, b) => (a.tiles?.length ?? 0) - (b.tiles?.length ?? 0))[0];
      this.set({
        cursor: {
          lon: ll.lon, lat: ll.lat, elevation: doc.elevationAt(q.x, q.y), feature: area?.name ?? '',
          biome: doc.elevationAt(q.x, q.y) < 0 ? 'Sea' : (biomes.find((b) => b.id === doc.biomeAt(q.x, q.y))?.name ?? '') + (doc.landUseAt(q.x, q.y) ? ` (${landUseName(doc.landUseAt(q.x, q.y)) ?? ''})` : '') + this.capacityReadout(doc, q.x, q.y),
          kmPerPx: kmPerMu(q.y) / this.editor.camera.zoom,
        },
      });
    });
  }

  /** ` · Farming 143` while the capacity layer is on and the document has a capacity to read; else nothing. */
  private capacityReadout(doc: Editor['doc'], x: number, y: number): string {
    const view = this.editor.view();
    if (!view.layers.capacity || !doc.physics?.capacity) return '';
    const kind = CAPACITY_KINDS.find((k) => k.key === view.capacityKind)!;
    return ` · ${kind.label} ${capacityOf(doc.physics.capacity, doc.tileAt(x, y), kind.key)}`;
  }

  undo = (): void => this.editor.undo.undo();
  redo = (): void => this.editor.undo.redo();
  setTheme = (id: string): void => this.editor.setTheme(id);
  dismissError = (): void => this.set({ error: null });
  newBlank = (): void => this.editor.newBlank();

  open = async (): Promise<void> => {
    const f = await this.files.pick('.wmap,application/gzip,application/octet-stream');
    if (f) await this.editor.open(f).catch(() => {});
  };

  save = async (): Promise<void> => {
    const blob = await this.editor.save();
    this.files.save(blob, `${slug(this.editor.doc.settings.title)}.wmap`);
    this.set({ status: `Saved (${(blob.size / 1e6).toFixed(1)} MB)` });
  };

  exportSvg = async (): Promise<void> => {
    const blob = await this.editor.exportSvg(svgOptionsFromView(this.editor.view())).catch(() => null);
    if (blob) this.files.save(blob, `${slug(this.editor.doc.settings.title)}.svg`);
  };

  exportWkt = (): void => {
    this.files.save(this.editor.exportWkt(), `${slug(this.editor.doc.settings.title)}.wkt.csv`);
  };

  exportPng = async (): Promise<void> => {
    const blob = await this.editor.exportPng();
    this.files.save(blob, `${slug(this.editor.doc.settings.title)}.png`);
  };

  setTitle = (title: string): void => {
    this.editor.setSettings({ title });
    this.set({ title });
  };

  override dispose(): void { cancelAnimationFrame(this.raf); super.dispose(); }
}

const slug = (s: string) => s.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'map';
