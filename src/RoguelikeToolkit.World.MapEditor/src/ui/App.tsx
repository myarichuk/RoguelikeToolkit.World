import { useEffect, useState } from 'react';
import { GeneratePanel, GenerateFooter } from './GeneratePanel';
import { useApp, useViewModel } from './hooks';
import { MapView } from './MapView';
import { PacksPanel } from './PacksPanel';
import { StatusBar } from './StatusBar';
import { ThemePanel } from './ThemePanel';
import { Properties } from './Properties';
import { Toolbar } from './Toolbar';
import { TopBar } from './TopBar';
import { ViewPanel } from './ViewPanel';
import styles from './App.module.css';

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
    <div className={styles.app}>
      <TopBar />
      <Toolbar />
      <aside className={styles.left}>
        <div className={styles.tabs} role="tablist" onKeyDown={(e) => {
          const i = TABS.findIndex((t) => t.id === tab);
          const n = e.key === 'ArrowRight' ? i + 1 : e.key === 'ArrowLeft' ? i - 1 : e.key === 'Home' ? 0 : e.key === 'End' ? TABS.length - 1 : -1;
          if (n < 0) return;
          e.preventDefault();
          const next = TABS[(n + TABS.length) % TABS.length].id;
          setTab(next);
          document.getElementById(`tab-${next}`)?.focus();
        }}>
          {TABS.map((t) => <button key={t.id} id={`tab-${t.id}`} role="tab" aria-selected={tab === t.id} aria-controls="side-panel" tabIndex={tab === t.id ? 0 : -1} className={styles.tab} onClick={() => setTab(t.id)}>{t.label}</button>)}
        </div>
        <div className={styles.tabpanel} id="side-panel" role="tabpanel" aria-labelledby={`tab-${tab}`}>
          <div className={styles.scroll}><Active /></div>
          {tab === 'world' && <GenerateFooter />}
        </div>
      </aside>
      <main className={styles.center}><MapView /></main>
      <Properties />
      <StatusBar />
      {shell.error && <div className={styles.toast} role="alert"><b>Error</b><pre>{shell.error}</pre><button className="dismiss" onClick={vms.shell.dismissError}><small>Dismiss</small></button></div>}
    </div>
  );
}

function safeGet(k: string): string | null { try { return localStorage.getItem(`wme.${k}`); } catch { return null; } }
function safeSet(k: string, v: string): void { try { localStorage.setItem(`wme.${k}`, v); } catch { /* storage unavailable */ } }
