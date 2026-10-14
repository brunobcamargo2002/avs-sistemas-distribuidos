# Interface web

Front-end da loja: consulta o catálogo, monta o carrinho, cria pedidos e acompanha
o status de cada um. É HTML, CSS e JavaScript puros — sem framework e sem etapa de
build obrigatória. O [Vite](https://vite.dev) entra apenas como servidor de
desenvolvimento, em processo próprio, separado da API que a página consome.

## Pré-requisitos

- **Node.js 24 LTS** (ou outra linha par em LTS). O Vite 7 exige `^20.19.0 || >=22.12.0`.
- O **ms-principal (`orders`) e o ms-estoque (`stock`) no ar**, além do RabbitMQ.
  Veja [`avaliacao-2/readme.md`](../avaliacao-2/readme.md).

## Como rodar

```bash
cd interface
npm install     # só na primeira vez, ou quando o package.json mudar
npm run dev
```

Acesse **<http://localhost:5173>**.

Use `localhost` e não `127.0.0.1`: para o navegador são origens diferentes, e o
`orders` libera CORS apenas para `http://localhost:5173`.

A porta é fixa (`strictPort`). Se 5173 estiver ocupada o Vite falha em vez de
trocar de porta silenciosamente — o que quebraria o CORS sem explicação clara.

## De quem o front depende

| Chamada | Serviço | Porta |
| --- | --- | --- |
| `GET /api/products` | `orders` (que repassa ao `stock`) | 5080 |
| `GET /api/orders?customerId=…` | `orders` | 5080 |
| `POST /api/orders` | `orders` | 5080 |

Os pedidos são repolados a cada 5 segundos, por isso o status muda na tela sozinho
conforme os eventos percorrem o RabbitMQ (`Criado` → `Estoque confirmado` →
`Pagamento aprovado` → `Enviado`). Para ver o fluxo até o fim, suba também os
serviços `payment` e `delivery`.

## Apontando para outra URL de API

O padrão é `http://localhost:5080`. Para mudar, defina `VITE_API_URL` antes do
`npm run dev`:

```bash
VITE_API_URL=http://localhost:5085 npm run dev     # bash
$env:VITE_API_URL = "http://localhost:5085"; npm run dev     # PowerShell
```

Nesse caso o `orders` precisa liberar a origem do front via `FRONTEND_ORIGIN`
(aceita várias separadas por `;`):

```bash
FRONTEND_ORIGIN="http://localhost:5173;http://127.0.0.1:5173" dotnet run --project ../avaliacao-2/application/orders/orders.csproj
```

## Build de produção

```bash
npm run build     # gera dist/
npm run preview   # serve o dist/ para conferência
```

## Problemas comuns

| Sintoma | Causa provável |
| --- | --- |
| `Requisição cross-origin bloqueada … Código de status: (null)` | O `orders` **não está rodando**. Sem resposta, o navegador relata como falha de CORS. Suba o serviço em 5080. |
| Erro de CORS **com** status 200 | Aí é CORS de verdade: a origem do front não está em `FRONTEND_ORIGIN` (ex.: você abriu por `127.0.0.1`). |
| Catálogo vazio com aviso de serviço indisponível (503) | O `stock` não está no ar em 5081. |
| Abrir `index.html` por duplo clique não funciona | `file://` não é origem HTTP válida para as chamadas; sirva pelo Vite. |
