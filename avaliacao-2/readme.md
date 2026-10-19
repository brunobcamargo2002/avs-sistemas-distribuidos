# AVS Sistemas Distribuídos

## Microsserviços

Na pasta `avaliacao-2/application` estão os serviços independentes:

- `orders`: API REST da loja, publicação de pedidos e estado dos pedidos;
- `stock`: API REST do catálogo, reserva e devolução no `product-catalog.json`;
- `payment`: cria a cobrança no provedor externo, repassa o link e publica o resultado do webhook;
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
- `delivery/Keys/payment_public_key.pem`;
- `promotions/consumer-promotions/Keys/public_key.pem` do publicador e `orders_public_key.pem` do Gateway.

## Execução

Com o RabbitMQ disponível em `localhost` e usuário/senha `admin`:

Os projetos exigem SDK .NET 10 ou superior. O script Bash deve ser executado em Linux/WSL com os comandos e terminais graficos necessarios disponiveis. Antes de abrir os servicos, `--run` e `--all` verificam o SDK ativo, as variaveis `RESEND_API_KEY` e `PROMOTIONS_FROM_EMAIL` e as chaves publicas usadas pelo consumidor de promocoes. As credenciais nao sao exibidas pelo script. Use `--setup` para preparar as chaves e o RabbitMQ sem iniciar os servicos.

O evento de indisponibilidade segue o PDF: `estoque.indisponivel`, publicado pelo estoque e consumido pelo Gateway. Reinicie os dois servicos em conjunto apos atualizar. O nome do evento SSE da interface permanece `estoque-indisponivel`; ele e independente da routing key do RabbitMQ.

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
2. O link é repassado em `pagamento.pendente`; o `payment` não guarda nada sobre a cobrança.
3. O `orders` consome esse evento, grava o link no pedido e muda o status para `Aguardando pagamento`. O front exibe o botão "Pagar agora".
4. O cliente abre o link, clica em "Pagamento Aprovado" ou "Pagamento Recusado", e o mock dispara o webhook `POST /api/webhooks/payment`.
5. O `payment` publica `pagamento.aprovado` ou `pagamento.recusado` com os dados da notificação (inclusive o valor).

O fluxo aprovado termina em `Enviado`; uma recusa exclui o pedido e publica `pedido.excluido` para devolver a reserva. O link e o status ficam no `orders.json` (visão do lojista) e em `payment-mock/charges.json` (visão do provedor, criado na primeira execução).

## Promocoes por e-mail

O Gateway recebe `POST /api/promotion-interests` com `email` e `categories`, e `DELETE /api/promotion-interests` com `email`. Publica os eventos assinados `interesse.promocao` e `interesse.promocao.cancelado` na fila duravel `fila.interesses.promocao`.

O `consumer-promotions` persiste os interesses, consome as ofertas do publicador pela fila `fila.promocoes` e envia uma mensagem individual a cada destinatario interessado usando `POST https://api.resend.com/emails`. Categorias abrangem suas subcategorias: `hardware` inclui `hardware.placa_de_video`. O cancelamento remove todas as inscricoes daquele e-mail; uma requisicao de envio ja iniciada nao pode ser desfeita.

Configure no ambiente do consumidor, antes de inicia-lo:

| Variavel | Uso |
| --- | --- |
| `RESEND_API_KEY` | Chave da API do Resend; obrigatoria, nunca versionar. |
| `PROMOTIONS_FROM_EMAIL` | Remetente autorizado pelo Resend, como `Loja <ofertas@seu-dominio.com>`; obrigatorio. |
| `PROMOTION_INTERESTS_PATH` | Arquivo de interesses; padrao `promotion-interests.json`. |
| `PROMOTION_DELIVERIES_PATH` | Historico dos envios aceitos; padrao `promotion-deliveries.json`. |

Para enviar a clientes reais, configure um dominio verificado e o remetente no Resend. O remetente de testes do provedor possui restricoes de destinatarios. Criar uma conta e configurar a credencial nao e feito automaticamente pelo projeto. O consumidor tambem precisa de `Keys/public_key.pem` do publicador e `Keys/orders_public_key.pem` do Gateway; `./run.sh --setup` copia essas chaves.

O historico local evita repetir envios aceitos apos reinicios. Cada envio tambem usa uma chave de idempotencia por promocao e destinatario no Resend, valida por 24 horas, protegendo novas tentativas se o provedor aceitou a mensagem antes de uma falha local. Nao apague o historico durante reprocessamentos. A confirmacao da API significa aceite pelo provedor, nao entrega garantida na caixa de entrada.

Falhas temporarias de rede, timeout, limite de requisicoes e erros HTTP 5xx reenfileiram a promocao com pausa de cinco segundos. Os destinatarios ja atendidos sao ignorados na nova tentativa e os interesses sao conferidos novamente antes de cada envio. Rejeicoes permanentes, como HTTP 401, 403 e 422, preservam o evento assinado em `fila.promocoes.falhas`, com a routing key original e o status HTTP nos headers. Corrija a configuracao ou os dados antes de republicar o evento na exchange `promotions` usando sua routing key original. Nao ha reprocessamento automatico dessa fila.