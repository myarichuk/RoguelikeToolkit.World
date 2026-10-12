import { useEffect, useState } from 'react';
import { TOOLS } from '../viewmodels/ToolsViewModel';
import type { ToolId } from '../engine/tools/Tool';
import { useApp, useViewModel } from './hooks';
import { Inspector } from './Inspector';
import { ToolOptionsPanel } from './Toolbar';

/** Brushes have nothing to select; a leftover selection must not hide their options. */
const BRUSHES: ReadonlySet<ToolId> = new Set(['terrain', 'biome', 'landuse', 'climate']);

/** One subject at a time: the selection if there is one (and the tool is not a brush), else the active tool. */
function useSubject(): { selection: boolean; label: string } {
  const { vms } = useApp();
  const tool = useViewModel(vms.tools).tool;
  const mode = useViewModel(vms.inspector).mode;
  const selection = mode !== 'none' && !BRUSHES.has(tool);
  return { selection, label: selection ? 'Selection' : TOOLS.find((t) => t.id === tool)!.label };
}

/**
 * The right column. A fixed side panel on wide screens; below 900px a bottom sheet whose handle names
 * the subject and which opens by itself when something gets selected.
 */
export function Properties() {
  const { selection, label } = useSubject();
  const [open, setOpen] = useState(false);
  useEffect(() => { if (selection) setOpen(true); }, [selection]);
  return (
    <aside className={`right ${open ? 'open' : ''}`} aria-label="Properties">
      <button className="sheet-handle" aria-expanded={open} onClick={() => setOpen(!open)}>
        <span aria-hidden="true">{open ? '▾' : '▴'}</span>Properties · {label}
      </button>
      <div className="scroll">{selection ? <Inspector /> : <ToolOptionsPanel />}</div>
    </aside>
  );
}
