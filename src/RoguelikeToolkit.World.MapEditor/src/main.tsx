import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { Editor } from './engine/Editor';
import { IndexedDbSessionStore } from './engine/doc/SessionStore';
import { App } from './ui/App';
import { browserFiles } from './ui/files';
import { AppContext } from './ui/hooks';
import './ui/global.css';
import { startFirstWorld } from './startup';
import { createViewModels } from './viewmodels';

// Composition root: one Editor (engine facade), one set of view models, one React tree.
const base = new URL(import.meta.env.BASE_URL, location.href).href;
const editor = new Editor({ frameworkUrl: new URL('_framework/dotnet.js', base).href, baseUrl: base, session: new IndexedDbSessionStore() });
const vms = createViewModels(editor, browserFiles);
(window as unknown as { editor: Editor }).editor = editor; // console access for debugging and tests

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppContext.Provider value={{ editor, vms }}>
      <App />
    </AppContext.Provider>
  </StrictMode>,
);

void startFirstWorld(editor, vms);
