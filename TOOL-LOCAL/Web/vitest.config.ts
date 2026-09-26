import { defineConfig } from 'vitest/config';

// Bound jsdom memory on the reference 16 GB Windows machine; keep file isolation.
export default defineConfig({
  test: { maxWorkers: 4, isolate: true }
});
