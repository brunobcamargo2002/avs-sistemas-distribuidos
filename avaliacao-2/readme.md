# AVS Sistemas Distribuídos

## Microsserviços

Na pasta `avaliacao-2/application` estão os serviços independentes:

- `orders`: interface de terminal e estado dos pedidos;
- `stock`: reserva e devolução no `product-catalog.json`;
- `payment`: simula aprovação ou recusa de pagamentos;
- `delivery`: emite nota, código de rastreio e o evento de envio.

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

A forma recomendada é usar o script da raiz do projeto. Ele configura as chaves, inicia/configura o RabbitMQ, compila os seis projetos e abre os serviços em terminais separados:

```bash
cd /home/rj/Documents/SistemasDistribuidos/avs-sistemas-distribuidos
./run.sh
```

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

O pagamento usa uma decisão aleatória: 80% de aprovação e 20% de recusa. O fluxo aprovado termina em `Enviado`; uma recusa exclui o pedido e publica `pedido.excluido` para devolver a reserva.