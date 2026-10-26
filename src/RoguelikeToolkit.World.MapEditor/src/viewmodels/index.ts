import type { Editor } from '../engine/Editor';
import { HistoryViewModel } from './HistoryViewModel';
import { HistoryWindowViewModel } from './HistoryWindowViewModel';
import { GenerateViewModel } from './GenerateViewModel';
import { InspectorViewModel } from './InspectorViewModel';
import { PacksViewModel } from './PacksViewModel';
import { PersonCardViewModel } from './PersonCardViewModel';
import { ShellViewModel } from './ShellViewModel';
import { ThemeEditorViewModel } from './ThemeEditorViewModel';
import { ToolsViewModel } from './ToolsViewModel';
import type { FileService } from './ViewModel';
import { WizardViewModel } from './WizardViewModel';
import { ViewFiltersViewModel } from './ViewFiltersViewModel';

export interface ViewModels {
  shell: ShellViewModel;
  generate: GenerateViewModel;
  filters: ViewFiltersViewModel;
  history: HistoryViewModel;
  historyWindow: HistoryWindowViewModel;
  person: PersonCardViewModel;
  tools: ToolsViewModel;
  inspector: InspectorViewModel;
  theme: ThemeEditorViewModel;
  packs: PacksViewModel;
  wizard: WizardViewModel;
}

/** One view model per panel, all over the same Editor facade. */
export function createViewModels(editor: Editor, files: FileService): ViewModels {
  const generate = new GenerateViewModel(editor);
  return {
    shell: new ShellViewModel(editor, files),
    generate,
    wizard: new WizardViewModel(editor, generate),
    filters: new ViewFiltersViewModel(editor),
    history: new HistoryViewModel(editor),
    historyWindow: new HistoryWindowViewModel(editor),
    person: new PersonCardViewModel(editor),
    tools: new ToolsViewModel(editor),
    inspector: new InspectorViewModel(editor),
    theme: new ThemeEditorViewModel(editor),
    packs: new PacksViewModel(editor, files),
  };
}

export const disposeViewModels = (vms: ViewModels): void => Object.values(vms).forEach((vm) => vm.dispose());
