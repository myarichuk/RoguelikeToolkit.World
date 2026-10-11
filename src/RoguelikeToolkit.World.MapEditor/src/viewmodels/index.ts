import type { Editor } from '../engine/Editor';
import { GenerateViewModel } from './GenerateViewModel';
import { InspectorViewModel } from './InspectorViewModel';
import { PacksViewModel } from './PacksViewModel';
import { ShellViewModel } from './ShellViewModel';
import { ThemeEditorViewModel } from './ThemeEditorViewModel';
import { ToolsViewModel } from './ToolsViewModel';
import type { FileService } from './ViewModel';
import { ViewFiltersViewModel } from './ViewFiltersViewModel';

export interface ViewModels {
  shell: ShellViewModel;
  generate: GenerateViewModel;
  filters: ViewFiltersViewModel;
  tools: ToolsViewModel;
  inspector: InspectorViewModel;
  theme: ThemeEditorViewModel;
  packs: PacksViewModel;
}

/** One view model per panel, all over the same Editor facade. */
export function createViewModels(editor: Editor, files: FileService): ViewModels {
  return {
    shell: new ShellViewModel(editor, files),
    generate: new GenerateViewModel(editor),
    filters: new ViewFiltersViewModel(editor),
    tools: new ToolsViewModel(editor),
    inspector: new InspectorViewModel(editor),
    theme: new ThemeEditorViewModel(editor),
    packs: new PacksViewModel(editor, files),
  };
}

export const disposeViewModels = (vms: ViewModels): void => Object.values(vms).forEach((vm) => vm.dispose());
