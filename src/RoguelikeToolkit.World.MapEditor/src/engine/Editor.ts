import { CompositeCommand, type Command } from './commands/Command';
import { AddEntities, RemoveEntities, SetValue, UpdateEntities } from './commands/EntityCommands';
import { UndoStack } from './commands/UndoStack';
import { AssetLibrary } from './content/AssetLibrary';
import corePack from './content/core-pack.json';
import { PackRegistry } from './content/PackRegistry';
import type { ContentPack, Theme } from './content/types';
import { Emitter } from './core/Emitter';
import { hash2, hashString } from './core/rng';
import { documentFromWorld } from './doc/fromWorld';
import { MapDocument } from './doc/MapDocument';
import { loadDocument, saveDocument } from './doc/serialize';
import type { DocSettings, Entity, FeatureEntity, LabelEntity, RiverEntity, StampEntity } from './doc/types';
import type { EntityStore } from './doc/EntityStore';
import { exportSvg, type SvgExportOptions } from './export/SvgExporter';
import { placeLabels } from './features/placeLabels';
import { populate } from './features/populate';
import { Camera } from './render/Camera';
import { MapRenderer, type FrameStats } from './render/MapRenderer';
import { Overlay } from './render/Overlay';
import { DEFAULT_VIEW, VIEW_PRESETS, type ViewSettings } from './render/ViewSettings';
import { FontLibrary } from './text/FontLibrary';
import { LabelCache } from './text/LabelCache';
import { BiomeTool, TerrainTool } from './tools/BrushTools';
import { InspectTool } from './tools/InspectTool';
import { NavigateTool } from './tools/NavigateTool';
import { RiverTool } from './tools/RiverTool';
import { SelectTool } from './tools/SelectTool';
import { Selection } from './tools/Selection';
import { StampTool } from './tools/StampTool';
import { TextTool } from './tools/TextTool';
import { DEFAULT_TOOL_OPTIONS, type Tool, type ToolContext, type ToolId, type ToolOptions } from './tools/Tool';
import { ToolController, type ToolHost } from './tools/ToolController';
import { styleIndexForPlate } from './world/features';
import { NameCtx, type GenerateParams, type NameRequest, type NameStyle } from './world/protocol';
import { WorldService } from './world/WorldService';

export interface GenerateRequest extends GenerateParams { title?: string; density?: number; }

export interface EditorEvents extends Record<string, unknown> {
  doc: MapDocument;
  theme: Theme;
  view: ViewSettings;
  tool: ToolId;
  options: ToolOptions;
  status: string;
  busy: string | null;
  error: string;
  content: PackRegistry;
  frame: FrameStats;
  pointer: { x: number; y: number } | null;
  shortcut: 'save' | 'open';
  timings: Record<string, number>;
}

export interface EditorConfig {
  /** URL of the .NET runtime entry (dotnet.js) published from the Engine project. */
  frameworkUrl: string;
  /** Base URL that the core pack's relative URLs (fonts) resolve against. */
  baseUrl: string;
}

/**
 * Facade and composition root. It owns the services (world worker, packs, atlas, fonts, label
 * cache, renderer, overlay, undo, selection, tools), wires their observers, and exposes the
 * editor's use cases as methods. View models talk to this class only; tools talk to it only
 * through ToolContext; nothing below it knows React exists.
 */
export class Editor implements ToolContext, ToolHost {
  readonly events = new Emitter<EditorEvents>();
  readonly packs = new PackRegistry();
  readonly library = new AssetLibrary();
  readonly fonts = new FontLibrary();
  readonly labels = new LabelCache(this.fonts);
  readonly undo = new UndoStack();
  readonly selection = new Selection();
  readonly world: WorldService;
  readonly tools: Record<ToolId, Tool> = {
    navigate: new NavigateTool(), inspect: new InspectTool(), select: new SelectTool(), terrain: new TerrainTool(),
    biome: new BiomeTool(), stamp: new StampTool(), text: new TextTool(), river: new RiverTool(),
  };

  private _doc = MapDocument.empty();
  private _themeId = 'parchment';
  private _view: ViewSettings = DEFAULT_VIEW;
  private _tool: ToolId = 'inspect';
  private _options: ToolOptions = { ...DEFAULT_TOOL_OPTIONS };
  private _styles: NameStyle[] = [];
  private renderer: MapRenderer | null = null;
  private overlay: Overlay | null = null;
  private controller: ToolController | null = null;
  private resize: ResizeObserver | null = null;
  private readonly fallbackCamera = new Camera();
  private contentToken = 0;
  private atlasKey = '';
  private contentTimer = 0;
  private lastFrameEmit = 0;
  private nameNonce = 1;
  private maskKey = '';
  /** Resolves when fonts and the atlas for the current theme are ready. */
  contentReady: Promise<void> = Promise.resolve();

  constructor(config: EditorConfig) {
    this.world = new WorldService(config.frameworkUrl);
    this.packs.add(corePack, config.baseUrl);
    this.packs.events.on('change', () => {
      this.events.emit('content', this.packs);
      // Debounced: a slider scrub in the theme editor is many changes.
      clearTimeout(this.contentTimer);
      this.contentTimer = window.setTimeout(() => { this.contentReady = this.refreshContent(); }, 60);
    });
    this.selection.events.on('change', () => this.selectionChanged());
    this.undo.events.on('change', () => this.requestRender());
    this.contentReady = this.refreshContent();
    void this.world.styles().then((s) => { this._styles = s; this.events.emit('content', this.packs); }).catch((e) => this.fail(e));
  }

  // --- state accessors ---------------------------------------------------------------------

  get doc(): MapDocument { return this._doc; }
  get camera(): Camera { return this.renderer?.camera ?? this.fallbackCamera; }
  get context(): ToolContext { return this; }
  get options(): Readonly<ToolOptions> { return this._options; }
  get toolId(): ToolId { return this._tool; }
  get themeId(): string { return this._themeId; }
  /** Every name style the engine knows (race x culture). */
  get nameStyles(): readonly NameStyle[] { return this._styles; }
  get stats(): FrameStats | null { return this.renderer?.stats ?? null; }
  theme(): Theme { return this.packs.theme(this._themeId); }
  view(): ViewSettings { return this._view; }
  activeTool(): Tool { return this.tools[this._tool]; }

  // --- mounting ----------------------------------------------------------------------------

  /** Binds the editor to its two canvases (WebGL map below, 2D overlay above, overlay gets input). */
  mount(glCanvas: HTMLCanvasElement, overlayCanvas: HTMLCanvasElement): void {
    this.unmount();
    const renderer = new MapRenderer(glCanvas);
    const overlay = new Overlay(overlayCanvas, this.labels);
    this.renderer = renderer; this.overlay = overlay;
    renderer.onAfterFrame = () => this.afterFrame();
    renderer.setView(this._view);
    renderer.attach(this._doc, this.library);
    void this.contentReady.then(() => this.applyTheme());
    this.controller = new ToolController(overlayCanvas, this);
    this.activeTool().activate?.(this);
    const host = overlayCanvas.parentElement ?? overlayCanvas;
    this.resize = new ResizeObserver(() => {
      const r = host.getBoundingClientRect();
      renderer.resize(r.width, r.height, window.devicePixelRatio || 1);
    });
    this.resize.observe(host);
  }

  unmount(): void {
    this.resize?.disconnect(); this.resize = null;
    this.controller?.dispose(); this.controller = null;
    this.renderer?.dispose(); this.renderer = null;
    this.overlay = null;
  }

  requestRender(): void { this.renderer?.requestRender(); }

  /** Centers the view on a map point; `extent` (map units) zooms so it fits comfortably. */
  focus(x: number, y: number, extent = 0): void {
    const cam = this.camera;
    cam.cx = x; cam.cy = y;
    if (extent > 0) cam.zoom = Math.min(cam.width, cam.height) / (extent * 3.5);
    else cam.zoom = Math.max(cam.zoom, 2);
    cam.constrain();
    this.requestRender();
  }

  private afterFrame(): void {
    const r = this.renderer!, o = this.overlay!;
    o.gizmos = this.activeTool().gizmo ? [this.activeTool().gizmo!] : [];
    o.draw(r.camera, this._doc, this.theme(), this._view);
    const now = performance.now();
    if (now - this.lastFrameEmit > 250) { this.lastFrameEmit = now; this.events.emit('frame', r.stats); }
  }

  // --- ToolContext -------------------------------------------------------------------------

  setOptions(patch: Partial<ToolOptions>): void {
    this._options = { ...this._options, ...patch };
    this.events.emit('options', this._options);
    this.requestRender();
  }

  setHiddenStamps(ids: Iterable<string>): void { this.renderer?.setHiddenStamps(ids); }
  status(text: string): void { this.events.emit('status', text); }
  pointer(x: number, y: number): void { this.events.emit('pointer', Number.isFinite(x) ? { x, y } : null); }
  shortcut(id: 'save' | 'open'): void { this.events.emit('shortcut', id); }
  undoLast(): void { this.undo.undo(); }
  redoLast(): void { this.undo.redo(); }

  /** A name in the culture that owns the plate under (x, y), flavored by what is around it. */
  async nameAt(kind: string, x: number, y: number): Promise<string> {
    const doc = this._doc;
    const [name] = await this.world.names([this.nameRequestAt(kind, x, y, hash2(hash2(doc.settings.seed, hashString(kind)), this.nameNonce++ * 7919 + (Math.round(x) << 12) + Math.round(y)))]);
    return name;
  }

  nameRequestAt(kind: string, x: number, y: number, seed: number): NameRequest {
    const doc = this._doc, styles = doc.world.styles;
    const t = doc.tileAt(x, y);
    const plate = t >= 0 && doc.world.tilePlate.length ? doc.world.tilePlate[t] & 0xffff : 0;
    const s = styles[styleIndexForPlate(doc.settings.seed, plate, styles.length)] ?? { race: 'human', culture: 'fantasy_common' };
    let context = 0;
    const h = doc.elevationAt(x, y), b = doc.biomeAt(x, y);
    for (let a = 0; a < 8; a++) if (doc.elevationAt(x + Math.cos(a) * 14, y + Math.sin(a) * 14) < 0) { context |= NameCtx.Water; break; }
    if (b === 3 || b === 6) context |= NameCtx.Forest;
    if (b === 7) context |= NameCtx.DarkForest;
    if (h > 0.45) context |= NameCtx.Highland;
    return { kind, seed: seed >>> 0, race: s.race, culture: s.culture, context };
  }

  // --- tools, view, theme ------------------------------------------------------------------

  setTool(id: ToolId): void {
    if (id === this._tool) return;
    this.activeTool().deactivate?.(this);
    this._tool = id;
    this.activeTool().activate?.(this);
    this.controller?.updateCursor();
    this.events.emit('tool', id);
    this.requestRender();
  }

  setView(view: ViewSettings): void {
    this._view = view;
    this.renderer?.setView(view);
    // Hidden things cannot stay selected.
    const f = this.selection.state.feature ? this._doc.features.get(this.selection.state.feature) : undefined;
    if (f && !view.kinds[f.kind]) this.selection.set({ feature: null });
    this.events.emit('view', view);
  }

  applyPreset(id: string): void {
    const p = VIEW_PRESETS.find((x) => x.id === id);
    if (p) this.setView(p.apply(this._view));
  }

  setTheme(id: string): void {
    this._themeId = id;
    this.contentReady = this.refreshContent();
  }

  /** Live theme edit: stored in the user overlay pack (non-destructive, exportable). */
  editTheme(theme: Theme): void {
    if (theme.id !== this._themeId) this._themeId = theme.id;
    this.packs.putTheme(theme);
  }

  addPack(raw: unknown, baseUrl: string): ContentPack { return this.packs.add(raw, baseUrl); }

  /** Rebuilds what depends on packs + theme. Latest call wins; the atlas only when inks or assets changed. */
  private async refreshContent(): Promise<void> {
    const token = ++this.contentToken;
    try {
      const theme = this.theme();
      await this.fonts.loadAll(this.packs.fonts());
      const assets = this.packs.assets();
      const atlasKey = JSON.stringify(theme.palette) + '|' + JSON.stringify([...assets.values()]);
      if (atlasKey !== this.atlasKey) {
        await this.library.build(assets, theme);
        if (token !== this.contentToken) return;
        this.atlasKey = atlasKey;
      }
      if (token !== this.contentToken) return;
      this.labels.clear();
      this.applyTheme();
    } catch (e) { this.fail(e); }
  }

  private applyTheme(): void {
    const theme = this.theme();
    if (this.renderer) {
      this.renderer.setTheme(theme, this.packs.biomes());
      this.renderer.assetsChanged();
    }
    this.events.emit('theme', theme);
  }

  // --- documents ---------------------------------------------------------------------------

  private attach(doc: MapDocument): void {
    this._doc = doc;
    this.selection.clear();
    this.selection.set({ hoverFeature: null });
    this.maskKey = '';
    this.undo.clear();
    this.renderer?.attach(doc, this.library);
    this.renderer?.camera.fit();
    this.events.emit('doc', doc);
    this.requestRender();
  }

  async generate(req: GenerateRequest): Promise<void> {
    await this.busy('Generating world…', async () => {
      const t0 = performance.now();
      const snap = await this.world.generate({ seed: req.seed >>> 0, size: req.size, plates: req.plates, styles: req.styles });
      const doc = documentFromWorld(snap, { title: req.title ?? `World ${req.seed}`, styles: req.styles });
      await this.contentReady;
      let t = performance.now();
      doc.stamps.reset(this.autoStamps(doc, req.density));
      const populateMs = performance.now() - t;
      t = performance.now();
      doc.labels.reset(placeLabels(doc, this.theme(), this.fonts, doc.settings.seed));
      const labelsMs = performance.now() - t;
      this.attach(doc);
      const timings = { ...snap.timings, populateMs, labelsMs, totalMs: performance.now() - t0 };
      this.events.emit('timings', timings);
      this.status(`Generated in ${Math.round(timings.totalMs)} ms — ${doc.features.size} features, ${doc.stamps.size} stamps, ${doc.labels.size} labels`);
    });
  }

  newBlank(): void { this.attach(MapDocument.empty()); this.status('Blank map: paint land with the terrain brush (mode "land").'); }

  async open(file: Blob): Promise<void> {
    await this.busy('Opening…', async () => {
      const doc = await loadDocument(file);
      this.attach(doc);
      this.status(`Opened "${doc.settings.title}"`);
    });
  }

  async save(): Promise<Blob> {
    const blob = await saveDocument(this._doc);
    this.undo.markSaved();
    return blob;
  }

  async exportSvg(options: SvgExportOptions): Promise<Blob> {
    if (!this.renderer) throw new Error('Editor is not mounted.');
    return this.busy('Exporting SVG…', async () => {
      this.renderer!.renderNow();
      const r = await exportSvg({
        doc: this._doc, theme: this.theme(), view: this._view, terrain: this.renderer!.terrain, library: this.library, labels: this.labels, fonts: this.fonts,
      }, options);
      this.events.emit('timings', r.timings);
      this.status(`SVG: ${(r.svg.length / 1e6).toFixed(1)} MB in ${Math.round(Object.values(r.timings).reduce((a, b) => a + b, 0))} ms`);
      return new Blob([r.svg], { type: 'image/svg+xml' });
    });
  }

  /** PNG of what is on screen (map + overlay), at device resolution. */
  async exportPng(): Promise<Blob> {
    const r = this.renderer, o = this.overlay;
    if (!r || !o) throw new Error('Editor is not mounted.');
    r.renderNow();
    const c = new OffscreenCanvas(r.canvas.width, r.canvas.height);
    const g = c.getContext('2d')!;
    g.drawImage(r.canvas, 0, 0);
    g.drawImage(o.canvas, 0, 0);
    return c.convertToBlob({ type: 'image/png' });
  }

  setSettings(patch: Partial<DocSettings>, key?: string): void {
    const doc = this._doc;
    const before = Object.fromEntries(Object.keys(patch).map((k) => [k, doc.settings[k as keyof DocSettings]])) as Partial<DocSettings>;
    this.undo.execute(new SetValue('Map settings', (v: Partial<DocSettings>) => doc.setSettings(v), before, patch, key));
  }

  // --- feature/entity use cases ------------------------------------------------------------

  /** Re-rolls generated names (all named features, or the given ones), keeping labels and rivers in step. */
  async rerollNames(featureIds?: string[]): Promise<void> {
    const doc = this._doc;
    const feats = (featureIds ? featureIds.map((id) => doc.features.get(id)!) : [...doc.features.all()])
      .filter((f): f is FeatureEntity => !!f && !!f.request);
    if (!feats.length) return;
    const nonce = this.nameNonce++;
    const requests = feats.map((f) => ({ ...f.request!, seed: hash2(f.request!.seed, nonce) }));
    const names = await this.world.names(requests);
    this.undo.execute(this.renameCommand(feats.length === 1 ? 'Re-roll name' : 'Re-roll names', feats.map((f, i) => ({ f, name: names[i], request: requests[i] }))));
  }

  renameFeature(id: string, name: string): void {
    const f = this._doc.features.get(id);
    if (!f || f.name === name) return;
    this.undo.execute(this.renameCommand('Rename', [{ f, name, request: f.request }]));
  }

  private renameCommand(label: string, items: { f: FeatureEntity; name: string; request?: NameRequest }[]): Command {
    const doc = this._doc;
    const fb: FeatureEntity[] = [], fa: FeatureEntity[] = [], lb: LabelEntity[] = [], la: LabelEntity[] = [];
    const rb: RiverEntity[] = [], ra: RiverEntity[] = [];
    for (const { f, name, request } of items) {
      fb.push(f); fa.push({ ...f, name, request });
      const l = doc.labels.get(`lb_${f.id}`);
      if (l) { lb.push(l); la.push({ ...l, text: name, feature: l.feature ? { ...l.feature, request } : l.feature }); }
      const r = f.riverId ? doc.rivers.get(f.riverId) : undefined;
      if (r) { rb.push(r); ra.push({ ...r, name }); }
    }
    return new CompositeCommand(label, [
      new UpdateEntities('features', doc.features, fb, fa),
      new UpdateEntities('labels', doc.labels, lb, la),
      new UpdateEntities('rivers', doc.rivers, rb, ra),
    ]);
  }

  /** Deletes a feature with everything drawn for it (label, river ribbon, settlement marker). */
  deleteFeature(id: string): void {
    const doc = this._doc, f = doc.features.get(id);
    if (!f) return;
    const parts: Command[] = [new RemoveEntities('feature', doc.features, [id])];
    if (doc.labels.get(`lb_${id}`)) parts.push(new RemoveEntities('label', doc.labels, [`lb_${id}`]));
    if (f.riverId && doc.rivers.get(f.riverId)) parts.push(new RemoveEntities('river', doc.rivers, [f.riverId]));
    if (doc.stamps.get(`st_${id}`)) parts.push(new RemoveEntities('marker', doc.stamps, [`st_${id}`]));
    this.selection.set({ feature: null, hoverFeature: null });
    this.undo.execute(new CompositeCommand(`Delete ${f.name || f.kind}`, parts));
  }

  updateLabels(ids: string[], patch: Partial<LabelEntity>, key?: string): void {
    this.updateEntities('Edit label', this._doc.labels, ids, patch, key);
  }

  updateStamps(ids: string[], patch: Partial<StampEntity>, key?: string): void {
    this.updateEntities('Edit stamp', this._doc.stamps, ids, patch, key);
  }

  private updateEntities<T extends Entity>(label: string, store: EntityStore<T>, ids: string[], patch: Partial<T>, key?: string): void {
    const before = ids.map((id) => store.get(id)).filter((x): x is T => !!x);
    if (!before.length) return;
    this.undo.execute(new UpdateEntities(label, store, before, before.map((e) => ({ ...e, ...patch })), key));
  }

  deleteSelected(): void {
    const s = this.selection.state, doc = this._doc;
    const parts: Command[] = [];
    if (s.stamps.size) parts.push(new RemoveEntities('stamps', doc.stamps, [...s.stamps]));
    if (s.labels.size) parts.push(new RemoveEntities('labels', doc.labels, [...s.labels]));
    if (!parts.length) { if (s.feature) this.deleteFeature(s.feature); return; }
    this.selection.set({ stamps: new Set(), labels: new Set() });
    this.undo.execute(new CompositeCommand('Delete', parts));
  }

  /** Re-runs auto-population (rule stamps + settlement markers), keeping hand-placed stamps. One undo step. */
  repopulate(density = 1): void {
    const doc = this._doc;
    const auto = [...doc.stamps.all()].filter((s) => s.id.startsWith('st_')).map((s) => s.id);
    const next = this.autoStamps(doc, density);
    this.undo.execute(new CompositeCommand('Re-populate', [new RemoveEntities('stamps', doc.stamps, auto), new AddEntities('stamps', doc.stamps, next)]));
  }

  /** Re-places all feature labels (after theme typography changes or many edits). Hand-made labels stay. */
  relabel(): void {
    const doc = this._doc;
    const auto = [...doc.labels.all()].filter((l) => l.id.startsWith('lb_f_')).map((l) => l.id);
    const remove = new RemoveEntities('labels', doc.labels, auto);
    remove.do();
    const next = placeLabels(doc, this.theme(), this.fonts, doc.settings.seed);
    remove.undo();
    this.undo.execute(new CompositeCommand('Re-place labels', [remove, new AddEntities('labels', doc.labels, next)]));
  }

  private autoStamps(doc: MapDocument, density?: number): StampEntity[] {
    return populate(doc, { seed: doc.settings.seed, rules: this.packs.rules(), library: this.library, featureAssets: (k) => this.packs.featureAssets(k), density });
  }

  // --- selection -> renderer ---------------------------------------------------------------

  private selectionChanged(): void {
    const s = this.selection.state, doc = this._doc;
    if (this.overlay) this.overlay.selection = { labels: new Set(s.labels), stamps: new Set(s.stamps), feature: s.feature, hoverFeature: s.hoverFeature };
    const key = `${s.feature}|${s.hoverFeature}`;
    if (key !== this.maskKey) {
      this.maskKey = key;
      const tiles = (id: string | null) => {
        const f = id ? doc.features.get(id) : undefined;
        return f?.tiles && f.kind !== 'river' ? f.tiles : null;
      };
      this.renderer?.setMasks(tiles(s.feature), s.hoverFeature !== s.feature ? tiles(s.hoverFeature) : null);
    }
    this.requestRender();
  }

  // --- plumbing ----------------------------------------------------------------------------

  private async busy<T>(label: string, fn: () => Promise<T>): Promise<T> {
    this.events.emit('busy', label);
    try { return await fn(); } catch (e) { this.fail(e); throw e; } finally { this.events.emit('busy', null); }
  }

  private fail(e: unknown): void {
    const msg = e instanceof Error ? e.message : String(e);
    console.error(e);
    this.events.emit('error', msg);
  }

  dispose(): void {
    this.unmount();
    this.world.dispose();
  }
}

export const DEFAULT_GENERATE: GenerateRequest = { seed: 1337, size: 5, plates: 14, styles: [], density: 1 };
