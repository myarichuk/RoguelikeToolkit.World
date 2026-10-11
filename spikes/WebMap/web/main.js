import { MapGpu } from './gpu.js';
import { buildSvg } from './svg.js';

const $ = (id) => document.getElementById(id);
const canvas = $('map');
const gpu = new MapGpu(canvas);
const worker = new Worker('./worker.js', { type: 'module' });
let raster = null;
const timings = {};

function resize() {
  const r = canvas.getBoundingClientRect(), dpr = devicePixelRatio || 1;
  const w = Math.round(r.width * dpr), h = Math.round(r.height * dpr);
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
  gpu.render();
}
new ResizeObserver(resize).observe(canvas);

function stats(extra = '') {
  const t = (k) => timings[k] != null ? `${k.replace('Ms', '')} ${Math.round(timings[k])}ms` : null;
  $('stats').textContent = [raster && `${raster.tiles} tiles`, t('bootMs'), t('genMs'), t('pullMs'), t('rasterMs'), t('gpuMs'),
    gpu.jfaPasses && `${gpu.jfaPasses} JFA passes`, extra].filter(Boolean).join(' · ');
}

function rebuildGpu() {
  if (!raster) return;
  const fw = +$('field').value;
  gpu.build(raster, fw, fw / 2, { seed: +$('seed').value, warp: +$('warp').value, detail: +$('detail').value });
  timings.gpuMs = gpu.gpuMs;
  resize(); stats();
}

function generate() {
  $('go').disabled = true; $('svg').disabled = true;
  stats('generating…');
  // Raster at 2048x1024 regardless of field size: the planet carries ~1 tile per 5 px at size 6,
  // so more source pixels only interpolate the same triangles. Detail above it is the field's job.
  worker.postMessage({ seed: +$('seed').value, size: +$('size').value, width: 2048, height: 1024 });
}

worker.onmessage = (e) => {
  $('go').disabled = false;
  if (!e.data.ok) { $('stats').textContent = 'error: ' + e.data.error; console.error(e.data.error); return; }
  raster = e.data;
  Object.assign(timings, e.data.timings);
  if (timings.bootMs && timings._booted) delete timings.bootMs; // boot only counts once
  timings._booted = true;
  rebuildGpu();
  $('svg').disabled = false;
  window.__mapReady = true;
};

$('go').onclick = generate;
for (const id of ['warp', 'detail', 'field']) $(id).oninput = () => rebuildGpu();

$('svg').onclick = () => {
  const sdf = gpu.readSdf();
  const res = buildSvg(sdf, gpu.fieldW, gpu.fieldH, { ring: gpu.ring });
  stats(`svg ${Math.round(res.ms)}ms, ${(res.bytes / 1e6).toFixed(1)} MB, ${res.coastLines} coast rings`);
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([res.svg], { type: 'image/svg+xml' }));
  a.download = `world-${$('seed').value}.svg`;
  a.click();
  window.__lastSvg = res;
};

// Pan (drag) and zoom (wheel, anchored at the cursor). Horizontal pan wraps; it is a planet.
let drag = null;
canvas.addEventListener('pointerdown', (e) => { drag = { x: e.clientX, y: e.clientY }; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointermove', (e) => {
  if (!drag) return;
  const dpr = devicePixelRatio || 1;
  gpu.view.cx -= (e.clientX - drag.x) * dpr / gpu.view.zoom;
  gpu.view.cy -= (e.clientY - drag.y) * dpr / gpu.view.zoom;
  drag = { x: e.clientX, y: e.clientY };
  gpu.render();
});
canvas.addEventListener('pointerup', () => { drag = null; });
canvas.addEventListener('wheel', (e) => {
  e.preventDefault();
  const dpr = devicePixelRatio || 1, r = canvas.getBoundingClientRect();
  const mx = (e.clientX - r.left) * dpr - canvas.width / 2, my = (e.clientY - r.top) * dpr - canvas.height / 2;
  const v = gpu.view, before = { x: v.cx + mx / v.zoom, y: v.cy + my / v.zoom };
  v.zoom = Math.min(64, Math.max(0.1, v.zoom * Math.exp(-e.deltaY * 0.0015)));
  v.cx = before.x - mx / v.zoom; v.cy = before.y - my / v.zoom;
  gpu.render();
}, { passive: false });

window.__map = { gpu, generate, setView: (cx, cy, zoom) => { Object.assign(gpu.view, { cx, cy, zoom }); gpu.render(); } };
generate();
