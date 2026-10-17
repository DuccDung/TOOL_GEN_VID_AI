import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)), plugins: [react()], base: './',
  build: { rollupOptions: { input: {
    component: fileURLToPath(new URL('index.html', import.meta.url)),
    fullApp: fileURLToPath(new URL('full-app.html', import.meta.url))
  } } }
});
