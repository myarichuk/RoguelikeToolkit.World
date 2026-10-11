import { createContext, useContext, useSyncExternalStore } from 'react';
import type { Editor } from '../engine/Editor';
import type { ViewModels } from '../viewmodels';
import type { ViewModel } from '../viewmodels/ViewModel';

/** Binds a component to a view model: re-renders on state changes, nothing else. */
export function useViewModel<S extends object>(vm: ViewModel<S>): S {
  return useSyncExternalStore(vm.subscribe, vm.getSnapshot);
}

export const AppContext = createContext<{ editor: Editor; vms: ViewModels } | null>(null);

export function useApp(): { editor: Editor; vms: ViewModels } {
  const c = useContext(AppContext);
  if (!c) throw new Error('AppContext missing');
  return c;
}
