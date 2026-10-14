import { defineConfig } from "vite";

//Configuração do servidor vite para rodar local
export default defineConfig({
  server: {
    port: 5173,
    strictPort: true,
  },
});
