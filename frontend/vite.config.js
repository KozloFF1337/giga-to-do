import { defineConfig } from 'vite';
export default defineConfig({
  esbuild: { jsx: 'automatic' },
  build: { outDir: '../src/GigaTodo/wwwroot', emptyOutDir: true },
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': { target: 'http://127.0.0.1:5180', changeOrigin: false } }
  }
});
