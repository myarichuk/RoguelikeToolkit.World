import type { Theme } from '../content/types';
import type { AssetLibrary } from '../content/AssetLibrary';
import type { UndoStack } from '../commands/UndoStack';
import type { MapDocument } from '../doc/MapDocument';
import type { Camera } from '../render/Camera';
import type { GizmoPainter } from '../render/Overlay';
import type { ViewSettings } from '../render/ViewSettings';
import type { LabelCache } from '../text/LabelCache';
import type { Selection } from './Selection';
import type { HeightMode } from './brushes';

export type ToolId = 'navigate' | 'inspect' | 'select' | 'terrain' | 'biome' | 'stamp' | 'text' | 'river';

/** Per-tool settings, edited from the tool options panel. */
export interface ToolOptions {
  radius: number;
  strength: number;
  heightMode: HeightMode;
  biome: number;
  asset: string;
  /** Stamp brush: scatter spacing in map units (0 = single click placement). */
  scatter: number;
  stampScale: number;
  labelStyle: string;
}

export const DEFAULT_TOOL_OPTIONS: ToolOptions = {
  radius: 18, strength: 0.04, heightMode: 'raise', biome: 3, asset: 'core.mountain', scatter: 0, stampScale: 1, labelStyle: 'town',
};

export interface PointerInput {
  /** Map units (x unwrapped as seen on screen). */
  x: number; y: number;
  /** Device pixels. */
  sx: number; sy: number;
  button: number;
  shift: boolean; alt: boolean; ctrl: boolean;
}

/** What a tool may use. The Editor implements it; tools never reach into UI or React. */
export interface ToolContext {
  readonly doc: MapDocument;
  readonly undo: UndoStack;
  readonly camera: Camera;
  readonly library: AssetLibrary;
  readonly labels: LabelCache;
  readonly selection: Selection;
  readonly options: Readonly<ToolOptions>;
  setOptions(patch: Partial<ToolOptions>): void;
  theme(): Theme;
  view(): ViewSettings;
  requestRender(): void;
  setHiddenStamps(ids: Iterable<string>): void;
  /** Generates a name in the culture of the location (rivers, new labels). */
  nameAt(kind: string, x: number, y: number): Promise<string>;
  status(text: string): void;
}

/** Strategy: one interaction mode. All methods optional; return true from key() to consume. */
export interface Tool {
  readonly id: ToolId;
  readonly cursor: string;
  activate?(ctx: ToolContext): void;
  deactivate?(ctx: ToolContext): void;
  down?(p: PointerInput, ctx: ToolContext): void;
  move?(p: PointerInput, ctx: ToolContext): void;
  up?(p: PointerInput, ctx: ToolContext): void;
  hover?(p: PointerInput | null, ctx: ToolContext): void;
  key?(e: KeyboardEvent, ctx: ToolContext): boolean;
  readonly gizmo?: GizmoPainter;
}
