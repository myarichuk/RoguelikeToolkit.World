import type { Editor } from './engine/Editor';
import type { ViewModels } from './viewmodels';

/**
 * Decides what the editor shows first: the map from the last visit if one is stored; else the new-world wizard
 * (the engine boots behind it); or, if the author turned the wizard off, a world generated as soon as the engine is up.
 */
export async function startFirstWorld(editor: Editor, vms: ViewModels): Promise<void> {
  const restored = await editor.session.restore().catch(() => false);
  if (restored) return;
  if (vms.wizard.opensOnStart) { vms.wizard.show(); return; }
  await editor.world.ready.then(() => vms.generate.generate(), () => {});
}
