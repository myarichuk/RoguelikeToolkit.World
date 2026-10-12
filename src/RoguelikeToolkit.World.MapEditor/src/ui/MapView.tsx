import { useEffect, useRef } from 'react';
import { useApp } from './hooks';

/** Hosts the two canvases. The editor owns everything drawn on them; React only provides the elements. */
export function MapView() {
  const { editor } = useApp();
  const gl = useRef<HTMLCanvasElement>(null);
  const overlay = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    try { editor.mount(gl.current!, overlay.current!); } catch (e) { editor.events.emit('error', (e as Error).message); }
    return () => editor.unmount();
  }, [editor]);
  return (
    <div className="mapview">
      <canvas ref={gl} className="gl" />
      <canvas ref={overlay} className="overlay" tabIndex={0} aria-label="Map" />
    </div>
  );
}
