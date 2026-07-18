import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    // ビルド成果物は ngolRoot 配下からの相対パスで配置
    // ビルド後のコピーは手動 or postcopy スクリプト
    outDir: 'dist',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      // 開発時はホスト側のサーバー (11156) へ WebSocket を転送
      '/ws': {
        target: 'ws://127.0.0.1:11156',
        ws: true,
        changeOrigin: true,
      },
    },
  },
})
