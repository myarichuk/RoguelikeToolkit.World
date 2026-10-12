import type { ReactNode } from 'react';
import type { ToolId } from '../engine/tools/Tool';

// One icon set: 24px grid, 1.75 stroke, round caps, currentColor. Inline SVG so it renders the same
// on every OS, unlike the Unicode glyphs it replaces (some of which become colour emoji).
const PATHS: Record<ToolId, ReactNode> = {
  inspect: <><circle cx="10.5" cy="10.5" r="5.5" /><path d="m15 15 5 5" /></>,
  select: <path d="M5 4v14l4-4 3 6 3-1.5-3-5.5h5z" />,
  navigate: <path d="M12 3v18M3 12h18M12 3l-2.5 2.5M12 3l2.5 2.5M12 21l-2.5-2.5M12 21l2.5-2.5M3 12l2.5-2.5M3 12l2.5 2.5M21 12l-2.5-2.5M21 12l-2.5 2.5" />,
  terrain: <path d="m3 19 6-11 4 7 2-3 6 7z" />,
  biome: <path d="M12 21v-8M12 13c-4 0-6-3-6-7 4 0 6 3 6 7ZM12 15c3 0 5-2 5-5-3 0-5 2-5 5Z" />,
  landuse: <><rect x="4" y="4" width="16" height="16" rx="1" /><path d="M4 12h16M12 4v16" /></>,
  climate: <><circle cx="12" cy="12" r="4" /><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M5.6 18.4 7 17M17 7l1.4-1.4" /></>,
  stamp: <path d="M12 3 7 11h3l-4 6h5v4h2v-4h5l-4-6h3z" />,
  text: <path d="M5 6V4h14v2M12 4v16M9 20h6" />,
  river: <path d="M4 8c3-3 5 3 8 0s5 3 8 0M4 16c3-3 5 3 8 0s5 3 8 0" />,
};

export function ToolIcon({ id }: { id: ToolId }) {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {PATHS[id]}
    </svg>
  );
}
