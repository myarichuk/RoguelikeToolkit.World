import type { Editor } from '../engine/Editor';
import type { BiomeDef } from '../engine/content/types';
import type { PhysicsMode } from '../engine/world/derivePhysics';
import type { ClimateMode, HeightMode } from '../engine/tools/brushes';
import type { ToolId, ToolOptions } from '../engine/tools/Tool';
import { LAND_USE_SLOTS } from '../engine/world/landuse';
import { LANDUSE_HINTS } from '../ui/glossary';
import { ViewModel } from './ViewModel';

export interface ToolInfo { id: ToolId; label: string; key: string; hint: string; icon: string; }

export const TOOLS: ToolInfo[] = [
  { id: 'inspect', label: 'Inspect', key: 'I', icon: '⌕', hint: 'Hover and click map features (rivers, ranges, seas, cities, resources) to see details.' },
  { id: 'select', label: 'Select', key: 'V', icon: '➚', hint: 'Select, move, rotate (R), scale ([ ]), flip (F) and delete stamps and labels. Drag on empty map to box-select.' },
  { id: 'navigate', label: 'Pan', key: 'H', icon: '✥', hint: 'Drag to pan. Wheel zooms everywhere; middle/right drag or Space+drag pans in every tool.' },
  { id: 'terrain', label: 'Terrain', key: 'B', icon: '⛰', hint: 'Sculpt height: raise, lower, smooth, flatten, roughen; paint land or sea. Alt inverts raise/lower. [ ] brush size.' },
  { id: 'biome', label: 'Biome', key: 'G', icon: '❀', hint: 'Paint biomes. Alt+click picks the biome under the cursor.' },
  { id: 'landuse', label: 'Land use', key: 'L', icon: '▦', hint: LANDUSE_HINTS.tool },
  { id: 'climate', label: 'Climate', key: 'C', icon: '☀', hint: 'Paint warmer/cooler or wetter/drier air. Nothing changes until you apply physics; then biomes, rivers and lakes follow. Alt inverts. Tinted while this tool is active.' },
  { id: 'stamp', label: 'Stamp', key: 'S', icon: '♣', hint: 'Place decals. Scatter > 0 paints a spray of variants. Alt+drag erases.' },
  { id: 'text', label: 'Text', key: 'T', icon: 'A', hint: 'Click to add a label named in the local culture; click a label to select it.' },
  { id: 'river', label: 'River', key: 'W', icon: '〰', hint: 'Drag from source to mouth to draw a named river. Alt+click deletes one.' },
];

export const HEIGHT_MODES: { id: HeightMode; label: string }[] = [
  { id: 'raise', label: 'Raise' }, { id: 'lower', label: 'Lower' }, { id: 'smooth', label: 'Smooth' }, { id: 'flatten', label: 'Flatten' },
  { id: 'roughen', label: 'Roughen' }, { id: 'land', label: 'Paint land' }, { id: 'sea', label: 'Paint sea' },
];

export const CLIMATE_LAYERS: { id: ToolOptions['climateLayer']; label: string; hint: string }[] = [
  { id: 'temperature', label: 'Temperature', hint: 'Annual-mean temperature offset. Full strength is about 22 K. Biomes, snow line and evaporation follow it.' },
  { id: 'rain', label: 'Rainfall', hint: 'Annual precipitation offset. Wetter tiles grow forests and feed rivers and lakes; drier ones turn to steppe and desert.' },
];

/** Labels follow the layer: raising temperature is "warmer", raising rainfall is "wetter". */
export const climateModes = (layer: ToolOptions['climateLayer']): { id: ClimateMode; label: string; hint: string }[] => [
  { id: 'raise', label: layer === 'temperature' ? 'Warmer' : 'Wetter', hint: 'Adds to the offset under the brush; repeat strokes build up.' },
  { id: 'lower', label: layer === 'temperature' ? 'Cooler' : 'Drier', hint: 'Subtracts from the offset under the brush.' },
  { id: 'erase', label: 'Erase', hint: 'Fades the offset back toward zero (the generated climate).' },
];

export interface AssetOption { id: string; name: string; category: string; thumb: string; }

export interface ToolsState {
  tool: ToolId;
  options: ToolOptions;
  biomes: BiomeDef[];
  /** Theme color per biome id (styled view swatches). */
  biomeColors: Record<string, string>;
  assets: AssetOption[];
  labelStyles: string[];
  /** The document has an engine world behind it, so brush work can be turned into new rivers, lakes and climate. */
  physicsReady: boolean;
  busy: boolean;
}

/** Tool palette and the active tool's options (brush, biome palette, stamp palette, label style). */
export class ToolsViewModel extends ViewModel<ToolsState> {
  constructor(private readonly editor: Editor) {
    super({ tool: editor.toolId, options: editor.options, biomes: [], biomeColors: {}, assets: [], labelStyles: [], physicsReady: false, busy: false });
    this.track(editor.events.on('tool', (tool) => this.set({ tool })));
    this.track(editor.events.on('options', (options) => this.set({ options })));
    this.track(editor.events.on('theme', () => this.syncContent()));
    this.track(editor.events.on('doc', (doc) => this.set({ physicsReady: !!doc.physics && doc.hasWorld })));
    this.track(editor.events.on('busy', (b) => this.set({ busy: !!b })));
    this.syncContent();
  }

  /** Content-derived palettes; thumbnails are the atlas' own vector symbols (theme-inked). */
  private syncContent(): void {
    const ed = this.editor, theme = ed.theme(), lib = ed.library;
    const assets: AssetOption[] = [];
    for (const def of ed.packs.assets().values()) {
      const e = lib.get(def.id, 0);
      if (!e) continue;
      const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${e.viewBox[0]} ${e.viewBox[1]}">${e.svg}</svg>`;
      assets.push({ id: def.id, name: def.name, category: def.category, thumb: `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}` });
    }
    this.set({
      biomes: ed.packs.biomes().filter((b) => b.id > 0 && !LAND_USE_SLOTS.has(b.id)),
      biomeColors: theme.biomes,
      assets,
      labelStyles: Object.keys(theme.labels),
    });
  }

  setTool = (id: ToolId): void => this.editor.setTool(id);
  setOptions = (patch: Partial<ToolOptions>): void => this.editor.setOptions(patch);
  pickAsset = (asset: string): void => { this.editor.setOptions({ asset }); this.editor.setTool('stamp'); };
  applyPhysics = (mode: PhysicsMode): Promise<void> => this.editor.applyPhysics(mode).catch(() => {});
  pickBiome = (biome: number): void => { this.editor.setOptions({ biome }); this.editor.setTool('biome'); };
}
