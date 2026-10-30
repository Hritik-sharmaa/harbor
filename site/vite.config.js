import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Builds into ../docs (GitHub Pages). The screenshots already live there, so dev serves docs/ as public
// and the build leaves everything else in docs/ alone.
export default defineConfig({
  plugins: [react()],
  base: './',
  publicDir: '../docs',
  build: {
    outDir: '../docs',
    emptyOutDir: false,
    copyPublicDir: false,
    rolldownOptions: {
      // fixed names, so rebuilds overwrite instead of piling up hashed files in docs/assets
      output: { entryFileNames: 'assets/app.js', chunkFileNames: 'assets/[name].js', assetFileNames: 'assets/[name][extname]' },
    },
  },
})
