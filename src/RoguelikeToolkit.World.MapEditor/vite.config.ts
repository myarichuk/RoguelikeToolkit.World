import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// public/_framework (the .NET engine) is copied in by ../build.sh; Vite serves it verbatim and
// never bundles it — the world worker imports dotnet.js at runtime.
export default defineConfig({
  base: './',
  plugins: [react()],
  worker: { format: 'es' },
  build: { target: 'es2022', sourcemap: true },
  test: { environment: 'node', include: ['src/**/*.test.ts'] },
});
