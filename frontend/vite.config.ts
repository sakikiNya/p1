import { defineConfig } from 'vite';
export default defineConfig({server:{proxy:{'/api':{target:process.env.DEV_API_TARGET || 'http://127.0.0.1:8080',rewrite:path=>path.replace(/^\/api/,'')}}}});
