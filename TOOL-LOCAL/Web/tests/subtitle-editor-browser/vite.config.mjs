import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  plugins: [{
    name: 'measure-fixture-row-renders', enforce: 'pre',
    transform(code, id) {
      if (!id.endsWith('/VietsubSubtitleEditor.tsx')) return;
      return code.replace('}: VietsubCueRowProps) {',
        '}: VietsubCueRowProps) { (window as any).__rowRenders = ((window as any).__rowRenders ?? 0) + 1;');
    }
  }, react()],
  build: { emptyOutDir: false }
});
