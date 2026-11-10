import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import type { IncomingMessage, ServerResponse } from 'http'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    host: true,
    port: 5174,
      proxy: {
        '/api': {
          target: process.env.VITE_DEV_PROXY_TARGET || 'http://localhost:5050',
          changeOrigin: true,
          // Inject API key for dev - all API calls from the frontend get authenticated
          configure: (proxy) => {
            const devApiKey = process.env.VITE_DEV_API_KEY || 'ApiKey dev-key-full';
            proxy.on('proxyReq', (proxyReq) => {
              proxyReq.setHeader('Authorization', devApiKey);
            });
            proxy.on('proxyRes', (proxyRes: IncomingMessage, _req: IncomingMessage, res: ServerResponse) => {
            const contentType = proxyRes.headers['content-type'] ?? '';
            if (contentType.includes('text/event-stream')) {
              // Flush headers immediately and pipe directly - no buffering
              res.setHeader('Cache-Control', 'no-cache');
              res.setHeader('Connection', 'keep-alive');
              res.setHeader('X-Accel-Buffering', 'no');
              // Ensure flush is called on each chunk
              proxyRes.on('data', () => {
                if (typeof (res as any).flush === 'function') {
                  (res as any).flush();
                }
              });
            }
          });
        },
      },
    },
  },
})
