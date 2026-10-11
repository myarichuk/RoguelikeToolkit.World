import { useEffect, useState } from 'react';
import { GeneratePanel } from './GeneratePanel';
import { useApp, useViewModel } from './hooks';
import { Inspector } from './Inspector';
import { MapView } from './MapView';
import { PacksPanel } from './PacksPanel';
import { StatusBar } from './StatusBar';
import { ThemePanel } from './ThemePanel';
import { Toolbar, ToolOptionsPanel } from './Toolbar';
import { TopBar } from './TopBar';
import { ViewPanel } from './ViewPanel';

const TABS = [
  { id: 'world', label: 'World', View: GeneratePanel },
  { id: 'view', label: 'View', View: ViewPanel },
  { id: 'theme', label: 'Theme', View: ThemePanel },
  { id: 'packs', label: 'Packs', View: PacksPanel },
] as const;

export function App() {
  const { vms } = useApp();
  const shell = useViewModel(vms.shell);
  const [tab, setTab] = useState<(typeof TABS)[number]['id']>(() => (safeGet('tab') as never) ?? 'world');
  useEffect(() => { safeSet('tab', tab); }, [tab]);
  // Theme chrome: the theme's `ui` colors become CSS custom properties.
  useEffect(() => {
    const root = document.documentElement;
    for (const [k, v] of Object.entries(shell.chrome)) root.style.setProperty(k, v);
  }, [shell.chrome]);
  const Active = TABS.find((t) => t.id === tab)!.View;
  return (
    <div className="app">
      <TopBar />
      <Toolbar />
      <aside className="left">
        <div className="tabs" role="tablist">
          {TABS.map((t) => <button key={t.id} role="tab" aria-selected={tab === t.id} className={tab === t.id ? 'active' : ''} onClick={() => setTab(t.id)}>{t.label}</button>)}
        </div>
        <div className="scroll"><Active /></div>
      </aside>
      <main className="center"><MapView /></main>
      <aside className="right"><div className="scroll"><ToolOptionsPanel /><Inspector /></div></aside>
      <StatusBar />
      {shell.error && <div className="toast" onClick={vms.shell.dismissError} role="alert"><b>Error</b><pre>{shell.error}</pre><small>click to dismiss</small></div>}
    </div>
  );
}

function safeGet(k: string): string | null { try { return localStorage.getItem(`wme.${k}`); } catch { return null; } }
function safeSet(k: string, v: string): void { try { localStorage.setItem(`wme.${k}`, v); } catch { /* storage unavailable */ } }
