import { Emitter } from '../core/Emitter';
import type { Command } from './Command';

export interface UndoState { canUndo: boolean; canRedo: boolean; undoLabel?: string; redoLabel?: string; dirty: boolean; }

/** Linear history with a memory cap. `savedAt` tracks the clean point for "unsaved changes". */
export class UndoStack {
  private done: Command[] = [];
  private undone: Command[] = [];
  private savedAt: Command | null = null;
  readonly events = new Emitter<{ change: UndoState }>();

  constructor(private readonly limit = 200) {}

  /** Runs `cmd.do()` and records it. */
  execute(cmd: Command): void {
    cmd.do();
    this.record(cmd);
  }

  /** Records a command whose effect has already been applied (interactive strokes/drags). */
  record(cmd: Command): void {
    const top = this.done[this.done.length - 1];
    if (!(top?.merge?.(cmd))) {
      this.done.push(cmd);
      if (this.done.length > this.limit) this.done.shift();
    }
    this.undone = [];
    this.changed();
  }

  undo(): void {
    const c = this.done.pop();
    if (!c) return;
    c.undo();
    this.undone.push(c);
    this.changed();
  }

  redo(): void {
    const c = this.undone.pop();
    if (!c) return;
    c.do();
    this.done.push(c);
    this.changed();
  }

  clear(): void { this.done = []; this.undone = []; this.savedAt = null; this.changed(); }
  markSaved(): void { this.savedAt = this.done[this.done.length - 1] ?? null; this.changed(); }

  get state(): UndoState {
    const top = this.done[this.done.length - 1] ?? null;
    return {
      canUndo: this.done.length > 0, canRedo: this.undone.length > 0,
      undoLabel: top?.label, redoLabel: this.undone[this.undone.length - 1]?.label,
      dirty: top !== this.savedAt,
    };
  }

  private changed(): void { this.events.emit('change', this.state); }
}
