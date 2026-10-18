# AVS Sistemas Distribuídos

## Microsserviços

Na pasta `avaliacao-2/application` estão os serviços independentes:

- `orders`: API REST da loja, publicação de pedidos e estado dos pedidos;
- `stock`: API REST do catálogo, reserva e devolução no `product-catalog.json`;
- `payment`: cria a cobrança no provedor externo, expõe o webhook de retorno e publica o resultado;
- `delivery`: emite nota, código de rastreio e o evento de envio.

Fora de `application`, em `avaliacao-2/payment-mock`, está o **Mock de Pagamento**: um sistema externo que simula o provedor. Ele não participa do RabbitMQ nem assina mensagens — conversa com o `payment` apenas por REST.

Todos usam a exchange RabbitMQ `ecommerce` (`direct`) e assinam as mensagens com RSA no header `signature`.

## Chaves

Cada serviço precisa de `Keys/private_key.pem`. As chaves privadas não devem ser versionadas. Gere um par para cada serviço, por exemplo:

```bash
openssl genrsa -out Keys/private_key.pem 2048
openssl rsa -in Keys/private_key.pem -pubout -out Keys/public_key.pem
```

Copie as chaves públicas conforme os consumidores:

- `orders/Keys/stock_public_key.pem`, `payment_public_key.pem` e `delivery_public_key.pem`;
- `stock/Keys/orders_public_key.pem`;
- `payment/Keys/stock_public_key.pem`;
- `delivery/Keys/payment_public_key.pem`.

## Execução

Com o RabbitMQ disponível em `localhost` e usuário/senha `admin`:

A forma recomendada é usar o script da raiz do projeto. Ele configura as chaves, inicia/configura o RabbitMQ, compila os sete projetos e abre os serviços em terminais separados:

```bash
cd /home/rj/Documents/SistemasDistribuidos/avs-sistemas-distribuidos
./run.sh
```

A interface web roda em processo próprio, fora do `run.sh`. Com os serviços iniciados, suba o front e acesse <http://localhost:5173>:

```bash
cd ../interface
npm install   # só na primeira vez
npm run dev
```

Ela permite consultar e filtrar o catálogo, montar o carrinho, criar pedidos e acompanhar seus status. O identificador do cliente é usado para separar a consulta dos pedidos. Instruções completas em [`interface/README.md`](../interface/README.md).

Endpoints REST usados pela interface:

- `GET http://localhost:5080/api/products`: catálogo consultado pelo serviço de pedidos no serviço de estoque;
- `GET http://localhost:5080/api/orders?customerId=cliente-demo`: pedidos do cliente;
- `POST http://localhost:5080/api/orders`: cria e publica um pedido;
- `GET http://localhost:5081/api/products`: catálogo exposto pelo serviço de estoque (somente produtos ativos).

Endpoints do pagamento:

- `POST http://localhost:5082/api/webhooks/payment`: webhook chamado pelo mock com `APROVADO` ou `RECUSADO`;
- `POST http://localhost:5083/api/charges`: criação da cobrança (chamado pelo `payment`);
- `GET http://localhost:5083/checkout/{chargeId}`: página de decisão aberta pelo cliente;
- `GET http://localhost:5083/api/charges`: cobranças registradas no provedor.

Também é possível executar etapas individualmente:

```bash
./run.sh --setup   # chaves e RabbitMQ
./run.sh --build   # compilação
./run.sh --run     # inicia os serviços
```

O script procura `konsole`, `xterm`, `gnome-terminal` ou `x-terminal-emulator` para abrir os processos interativos. O `xterm` é priorizado quando o projeto é executado dentro do VS Code instalado via Snap, pois evita conflitos de bibliotecas GTK.

```bash
dotnet run --project avaliacao-2/application/stock/stock.csproj
dotnet run --project avaliacao-2/application/payment/payment.csproj
dotnet run --project avaliacao-2/application/delivery/delivery.csproj
dotnet run --project avaliacao-2/application/orders/orders.csproj
```

## Fluxo de pagamento

1. O `payment` consome `pedido.estoque_ok` e chama `POST /api/charges` do mock **de forma síncrona**, informando a própria URL de webhook. O retorno é o link de checkout.
2. O link é persistido em `payments.json` com status `PENDENTE` e publicado em `pagamento.pendente`.
3. O `orders` consome esse evento, grava o link no pedido e muda o status para `Aguardando pagamento`. O front exibe o botão "Pagar agora".
4. O cliente abre o link, clica em "Pagamento Aprovado" ou "Pagamento Recusado", e o mock dispara o webhook `POST /api/webhooks/payment`.
5. O `payment` liquida o registro (`APROVADO`/`RECUSADO`) e publica `pagamento.aprovado` ou `pagamento.recusado`.

O fluxo aprovado termina em `Enviado`; uma recusa exclui o pedido e publica `pedido.excluido` para devolver a reserva. Os arquivos `payments.json` (pagamentos do lojista) e `payment-mock/charges.json` (cobranças do provedor) são criados na primeira execução.