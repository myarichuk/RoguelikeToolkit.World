// Command pattern: every document mutation is a Command, so undo/redo, dirty tracking and
// (later) collaboration all hang off one seam. Commands are executed by UndoStack.execute().

export interface Command {
  readonly label: string;
  do(): void;
  undo(): void;
  /**
   * Coalescing: return true if `next` was folded into this command (e.g. successive nudges of
   * the same selection). The stack then drops `next`.
   */
  merge?(next: Command): boolean;
}

/** Several commands as one undo step (Composite). */
export class CompositeCommand implements Command {
  constructor(readonly label: string, private readonly parts: Command[]) {}
  do(): void { for (const c of this.parts) c.do(); }
  undo(): void { for (let i = this.parts.length - 1; i >= 0; i--) this.parts[i].undo(); }
  get isEmpty(): boolean { return this.parts.length === 0; }
}
