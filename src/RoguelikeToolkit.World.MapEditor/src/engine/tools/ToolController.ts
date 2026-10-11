import type { Camera } from '../render/Camera';
import type { PointerInput, Tool, ToolContext, ToolId } from './Tool';

/** What the controller needs from its owner (the Editor). */
export interface ToolHost {
  readonly camera: Camera;
  readonly context: ToolContext;
  activeTool(): Tool;
  setTool(id: ToolId): void;
  requestRender(): void;
  undoLast(): void;
  redoLast(): void;
  shortcut(id: 'save' | 'open'): void;
  pointer(x: number, y: number): void;
}

const TOOL_KEYS: Record<string, ToolId> = { h: 'navigate', i: 'inspect', v: 'select', b: 'terrain', g: 'biome', s: 'stamp', t: 'text', w: 'river' };

/**
 * Turns DOM input into tool calls and camera moves. Navigation is universal (wheel zoom,
 * middle/right drag or Space+drag pan, two-finger pinch) so every tool keeps its left button.
 * Keyboard: the active tool sees keys first; then undo/redo, save/open, tool shortcuts and
 * brush-size brackets.
 */
export class ToolController {
  private panning: { id: number; x: number; y: number } | null = null;
  private down: number | null = null;
  private space = false;
  private readonly touches = new Map<number, { x: number; y: number }>();
  private pinch: { d: number; cx: number; cy: number } | null = null;
  private readonly off: (() => void)[] = [];

  constructor(private readonly el: HTMLElement, private readonly host: ToolHost) {
    const on = <K extends keyof HTMLElementEventMap>(t: EventTarget, type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions) => {
      t.addEventListener(type, fn as EventListener, opts);
      this.off.push(() => t.removeEventListener(type, fn as EventListener, opts));
    };
    on(el, 'pointerdown', (e) => this.onDown(e));
    on(el, 'pointermove', (e) => this.onMove(e));
    on(el, 'pointerup', (e) => this.onUp(e));
    on(el, 'pointercancel', (e) => this.onUp(e));
    on(el, 'pointerleave', () => { if (this.down === null) this.host.activeTool().hover?.(null, this.host.context); this.host.pointer(NaN, NaN); });
    on(el, 'wheel', (e) => this.onWheel(e), { passive: false });
    on(el, 'contextmenu', (e) => e.preventDefault());
    on(window, 'keydown', (e) => this.onKey(e));
    on(window, 'keyup', (e) => { if (e.code === 'Space') { this.space = false; this.updateCursor(); } });
    on(window, 'blur', () => { this.space = false; this.panning = null; });
    this.updateCursor();
  }

  dispose(): void { this.off.forEach((f) => f()); }

  updateCursor(): void {
    this.el.style.cursor = this.panning ? 'grabbing' : this.space ? 'grab' : this.host.activeTool().cursor;
  }

  private input(e: PointerEvent | WheelEvent): PointerInput {
    const r = this.el.getBoundingClientRect(), dpr = this.host.camera.dpr;
    const sx = (e.clientX - r.left) * dpr, sy = (e.clientY - r.top) * dpr;
    const m = this.host.camera.toMap(sx, sy);
    return { x: m.x, y: m.y, sx, sy, button: 'button' in e ? e.button : 0, shift: e.shiftKey, alt: e.altKey, ctrl: e.ctrlKey || e.metaKey };
  }

  private onDown(e: PointerEvent): void {
    this.el.focus({ preventScroll: true });
    if (e.pointerType === 'touch') {
      this.touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (this.touches.size === 2) { this.cancelStroke(e); this.pinch = this.pinchState(); return; }
    }
    const tool = this.host.activeTool();
    const pan = e.button === 1 || e.button === 2 || (e.button === 0 && (this.space || tool.id === 'navigate'));
    this.el.setPointerCapture(e.pointerId);
    if (pan) {
      this.panning = { id: e.pointerId, x: e.clientX, y: e.clientY };
      this.updateCursor();
      e.preventDefault();
      return;
    }
    if (e.button !== 0) return;
    this.down = e.pointerId;
    void tool.down?.(this.input(e), this.host.context);
  }

  private onMove(e: PointerEvent): void {
    if (e.pointerType === 'touch' && this.touches.has(e.pointerId)) {
      this.touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (this.pinch && this.touches.size === 2) { this.applyPinch(); return; }
    }
    const p = this.input(e);
    this.host.pointer(p.x, p.y);
    if (this.panning && this.panning.id === e.pointerId) {
      const dpr = this.host.camera.dpr;
      this.host.camera.panBy((e.clientX - this.panning.x) * dpr, (e.clientY - this.panning.y) * dpr);
      this.panning.x = e.clientX; this.panning.y = e.clientY;
      this.host.requestRender();
      return;
    }
    const tool = this.host.activeTool();
    if (this.down === e.pointerId) tool.move?.(p, this.host.context);
    else if (this.down === null) { tool.hover?.(p, this.host.context); if (!tool.hover) tool.move?.(p, this.host.context); }
  }

  private onUp(e: PointerEvent): void {
    this.touches.delete(e.pointerId);
    if (this.touches.size < 2) this.pinch = null;
    if (this.el.hasPointerCapture(e.pointerId)) this.el.releasePointerCapture(e.pointerId);
    if (this.panning?.id === e.pointerId) { this.panning = null; this.updateCursor(); return; }
    if (this.down === e.pointerId) {
      this.down = null;
      this.host.activeTool().up?.(this.input(e), this.host.context);
    }
  }

  /** A second finger turns a stroke into a pinch: finish the stroke where it is. */
  private cancelStroke(e: PointerEvent): void {
    if (this.down !== null) { this.host.activeTool().up?.(this.input(e), this.host.context); this.down = null; }
    this.panning = null;
  }

  private pinchState(): { d: number; cx: number; cy: number } {
    const [a, b] = [...this.touches.values()];
    return { d: Math.hypot(a.x - b.x, a.y - b.y), cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2 };
  }

  private applyPinch(): void {
    const next = this.pinchState(), prev = this.pinch!;
    const r = this.el.getBoundingClientRect(), dpr = this.host.camera.dpr, cam = this.host.camera;
    cam.panBy((next.cx - prev.cx) * dpr, (next.cy - prev.cy) * dpr);
    if (prev.d > 0) cam.zoomAt((next.cx - r.left) * dpr, (next.cy - r.top) * dpr, next.d / prev.d);
    this.pinch = next;
    this.host.requestRender();
  }

  private onWheel(e: WheelEvent): void {
    e.preventDefault();
    const p = this.input(e);
    const unit = e.deltaMode === 1 ? 0.05 : e.deltaMode === 2 ? 1 : 0.0015;
    this.host.camera.zoomAt(p.sx, p.sy, Math.exp(-e.deltaY * unit));
    this.host.requestRender();
  }

  private onKey(e: KeyboardEvent): void {
    const t = e.target as HTMLElement | null;
    if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
    const ctrl = e.ctrlKey || e.metaKey, k = e.key.toLowerCase();
    if (ctrl && k === 'z') { e.preventDefault(); if (e.shiftKey) this.host.redoLast(); else this.host.undoLast(); return; }
    if (ctrl && k === 'y') { e.preventDefault(); this.host.redoLast(); return; }
    if (ctrl && k === 's') { e.preventDefault(); this.host.shortcut('save'); return; }
    if (ctrl && k === 'o') { e.preventDefault(); this.host.shortcut('open'); return; }
    if (e.code === 'Space') { if (!this.space) { this.space = true; this.updateCursor(); } e.preventDefault(); return; }
    if (this.host.activeTool().key?.(e, this.host.context)) { e.preventDefault(); return; }
    if (ctrl || e.altKey) return;
    if (k === '[' || k === ']') {
      const ctx = this.host.context;
      ctx.setOptions({ radius: Math.max(2, Math.min(200, ctx.options.radius * (k === ']' ? 1.2 : 1 / 1.2))) });
      ctx.requestRender();
      return;
    }
    const id = TOOL_KEYS[k];
    if (id) { this.host.setTool(id); this.updateCursor(); }
  }
}
