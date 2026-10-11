import type { PointerInput, Tool, ToolContext } from './Tool';

/** Drag to pan. (Every tool also pans with the middle button or Space-drag; see ToolController.) */
export class NavigateTool implements Tool {
  readonly id = 'navigate' as const;
  readonly cursor = 'grab';
  private last: PointerInput | null = null;
  down(p: PointerInput): void { this.last = p; }
  move(p: PointerInput, ctx: ToolContext): void {
    if (!this.last) return;
    ctx.camera.panBy(p.sx - this.last.sx, p.sy - this.last.sy);
    this.last = p;
    ctx.requestRender();
  }
  up(): void { this.last = null; }
}
