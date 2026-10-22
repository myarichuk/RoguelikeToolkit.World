import { newId } from '../core/ids';
import { AddEntities } from '../commands/EntityCommands';
import { labelExistsAt } from '../doc/existsAt';
import type { LabelEntity } from '../doc/types';
import { pickLabel } from './pick';
import type { PointerInput, Tool, ToolContext } from './Tool';

/** Click a label to select it (edit in the inspector); click empty map to add one, named in the local culture. */
export class TextTool implements Tool {
  readonly id = 'text' as const;
  readonly cursor = 'text';

  async down(p: PointerInput, ctx: ToolContext): Promise<void> {
    const hit = pickLabel(ctx.doc, ctx.labels, ctx.theme(), p.x, p.y, (id) => labelExistsAt(ctx.doc, id, ctx.view().historyTurn));
    if (hit) { ctx.selection.set({ labels: new Set([hit]), stamps: new Set(), feature: null }); return; }
    const style = ctx.options.labelStyle;
    const nameKind = ({ capital: 'city', ocean: 'sea', region: 'forest', title: 'city' } as Record<string, string>)[style] ?? style;
    let text = 'New label';
    try { text = (await ctx.nameAt(nameKind, p.x, p.y)) || text; } catch { /* engine busy or kind unknown: keep placeholder */ }
    const label: LabelEntity = { id: newId('lb'), text, style, x: p.x, y: p.y, angle: 0, size: 0, spacing: NaN, curve: 0 };
    ctx.undo.execute(new AddEntities('Add label', ctx.doc.labels, [label]));
    ctx.selection.set({ labels: new Set([label.id]), stamps: new Set(), feature: null });
  }
}
