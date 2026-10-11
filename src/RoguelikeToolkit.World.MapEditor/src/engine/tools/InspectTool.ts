import { pickFeature } from './pick';
import type { PointerInput, Tool, ToolContext } from './Tool';

/** Hover highlights a map feature; click selects it for the inspector. */
export class InspectTool implements Tool {
  readonly id = 'inspect' as const;
  readonly cursor = 'help';
  hover(p: PointerInput | null, ctx: ToolContext): void {
    const f = p ? pickFeature(ctx.doc, p.x, p.y, ctx.camera.zoom, ctx.view()) : null;
    ctx.selection.set({ hoverFeature: f?.id ?? null });
  }
  down(p: PointerInput, ctx: ToolContext): void {
    const f = pickFeature(ctx.doc, p.x, p.y, ctx.camera.zoom, ctx.view());
    ctx.selection.set({ feature: f?.id ?? null, stamps: new Set(), labels: new Set() });
    if (f) ctx.status(`${f.name || f.kind} — ${f.kind}`);
  }
  key(e: KeyboardEvent, ctx: ToolContext): boolean {
    if (e.key === 'Escape') { ctx.selection.set({ feature: null }); return true; }
    return false;
  }
  deactivate(ctx: ToolContext): void { ctx.selection.set({ hoverFeature: null }); }
}
