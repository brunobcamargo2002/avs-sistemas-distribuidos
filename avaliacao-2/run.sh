#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$ROOT_DIR/application"
PROMOTIONS_DIR="$ROOT_DIR/promotions"
RABBITMQ_CONTAINER="${RABBITMQ_CONTAINER:-rabbitmq}"
RABBITMQ_IMAGE="${RABBITMQ_IMAGE:-rabbitmq:3-management}"
RABBITMQ_VOLUME="${RABBITMQ_VOLUME:-${RABBITMQ_CONTAINER}_data}"

usage() {
    cat <<EOF
Uso: $0 [opção]

Opções:
  --all       Configura chaves/RabbitMQ, compila e inicia todos os serviços (padrão)
  --setup     Gera/copiar chaves e inicia/configura o RabbitMQ
  --build     Compila os seis projetos
  --run       Inicia os serviços sem configurar nem compilar
  --help      Mostra esta ajuda

Variáveis opcionais:
  RABBITMQ_CONTAINER  Nome do container RabbitMQ (padrão: rabbitmq)
  RABBITMQ_IMAGE      Imagem Docker (padrão: rabbitmq:3-management)
EOF
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "Erro: comando '$1' não encontrado." >&2
        exit 1
    }
}

setup_keys() {
    require_command openssl

    local service private_key public_key
    for service in orders stock payment delivery; do
        mkdir -p "$APP_DIR/$service/Keys"
        private_key="$APP_DIR/$service/Keys/private_key.pem"
        public_key="$APP_DIR/$service/Keys/public_key.pem"

        if [[ ! -f "$private_key" ]]; then
            echo "Gerando chave privada: $service"
            openssl genrsa -out "$private_key" 2048 >/dev/null 2>&1
        fi
        if [[ ! -f "$public_key" ]]; then
            openssl rsa -in "$private_key" -pubout -out "$public_key" >/dev/null 2>&1
        fi
    done

    cp "$APP_DIR/orders/Keys/public_key.pem" "$APP_DIR/stock/Keys/orders_public_key.pem"
    cp "$APP_DIR/stock/Keys/public_key.pem" "$APP_DIR/payment/Keys/stock_public_key.pem"
    rm -f "$APP_DIR/payment/Keys/orders_public_key.pem"
    cp "$APP_DIR/stock/Keys/public_key.pem" "$APP_DIR/orders/Keys/stock_public_key.pem"
    cp "$APP_DIR/payment/Keys/public_key.pem" "$APP_DIR/orders/Keys/payment_public_key.pem"
    cp "$APP_DIR/delivery/Keys/public_key.pem" "$APP_DIR/orders/Keys/delivery_public_key.pem"
    cp "$APP_DIR/payment/Keys/public_key.pem" "$APP_DIR/delivery/Keys/payment_public_key.pem"

    mkdir -p "$PROMOTIONS_DIR/publisher-promotions/Keys" "$PROMOTIONS_DIR/consumer-promotions/Keys"
    local promotions_private="$PROMOTIONS_DIR/publisher-promotions/Keys/private_key.pem"
    local promotions_public="$PROMOTIONS_DIR/publisher-promotions/Keys/public_key.pem"
    if [[ ! -f "$promotions_private" ]]; then
        echo "Gerando chave privada: publisher-promotions"
        openssl genrsa -out "$promotions_private" 2048 >/dev/null 2>&1
    fi
    openssl rsa -in "$promotions_private" -pubout -out "$promotions_public" >/dev/null 2>&1
    cp "$promotions_public" "$PROMOTIONS_DIR/consumer-promotions/Keys/public_key.pem"
}

setup_rabbitmq() {
    require_command docker

    if docker ps -a --format '{{.Names}}' | grep -qx "$RABBITMQ_CONTAINER" && \
        ! docker ps --format '{{.Names}}' | grep -qx "$RABBITMQ_CONTAINER"; then
        echo "Removendo container RabbitMQ parado para limpar a inicialização anterior."
        docker rm -fv "$RABBITMQ_CONTAINER" >/dev/null
    fi

    docker volume create "$RABBITMQ_VOLUME" >/dev/null
    docker run --rm --user root -v "$RABBITMQ_VOLUME:/var/lib/rabbitmq" \
        --entrypoint sh "$RABBITMQ_IMAGE" \
        -c 'chown -R 999:999 /var/lib/rabbitmq && chmod 700 /var/lib/rabbitmq' >/dev/null

    if ! docker ps --format '{{.Names}}' | grep -qx "$RABBITMQ_CONTAINER"; then
        docker run -d --hostname "$RABBITMQ_CONTAINER" --name "$RABBITMQ_CONTAINER" \
            -p 5672:5672 -p 15672:15672 -v "$RABBITMQ_VOLUME:/var/lib/rabbitmq" \
            "$RABBITMQ_IMAGE" >/dev/null
    fi

    echo "Aguardando RabbitMQ iniciar..."
    local attempt
    for attempt in {1..30}; do
        if docker exec "$RABBITMQ_CONTAINER" rabbitmq-diagnostics -q check_running >/dev/null 2>&1; then
            break
        fi
        if [[ "$attempt" -eq 30 ]]; then
            echo "Erro: RabbitMQ não iniciou. Logs do container:" >&2
            docker logs --tail 80 "$RABBITMQ_CONTAINER" >&2
            exit 1
        fi
        sleep 2
    done

    if ! docker exec "$RABBITMQ_CONTAINER" rabbitmqctl list_users 2>/dev/null | awk '{print $1}' | grep -qx admin; then
        docker exec "$RABBITMQ_CONTAINER" rabbitmqctl add_user admin admin >/dev/null
    fi
    docker exec "$RABBITMQ_CONTAINER" rabbitmqctl set_user_tags admin administrator >/dev/null
    docker exec "$RABBITMQ_CONTAINER" rabbitmqctl set_permissions -p / admin '.*' '.*' '.*' >/dev/null
    echo "RabbitMQ pronto em localhost:5672 (admin/admin)."
}

build_projects() {
    require_command dotnet
    local projects=(
        "$APP_DIR/orders/orders.csproj"
        "$APP_DIR/stock/stock.csproj"
        "$APP_DIR/payment/payment.csproj"
        "$APP_DIR/delivery/delivery.csproj"
        "$PROMOTIONS_DIR/publisher-promotions/publisher-promotions.csproj"
        "$PROMOTIONS_DIR/consumer-promotions/consumer-promotions.csproj"
    )
    for project in "${projects[@]}"; do
        echo "Compilando ${project#$ROOT_DIR/}"
        dotnet build "$project"
    done
}

terminal_command() {
    local title="$1"
    local command="$2"

    if command -v konsole >/dev/null 2>&1; then
        konsole --new-tab -p tabtitle="$title" -e bash -lc "$command; printf '\nServiço encerrado. Pressione Enter para fechar.'; read -r" &
    elif command -v xterm >/dev/null 2>&1; then
        xterm -T "$title" -e bash -lc "$command; printf '\nServiço encerrado. Pressione Enter para fechar.'; read -r" &
    elif command -v gnome-terminal >/dev/null 2>&1; then
        env -u GTK_PATH -u GTK_EXE_PREFIX -u GTK_IM_MODULE_FILE \
            gnome-terminal --title="$title" -- bash -lc "$command; printf '\nServiço encerrado. Pressione Enter para fechar.'; read -r"
    elif command -v x-terminal-emulator >/dev/null 2>&1; then
        env -u GTK_PATH -u GTK_EXE_PREFIX -u GTK_IM_MODULE_FILE \
            x-terminal-emulator -T "$title" -e bash -lc "$command; printf '\nServiço encerrado. Pressione Enter para fechar.'; read -r" &
    else
        echo "Erro: nenhum terminal gráfico encontrado (gnome-terminal, konsole ou x-terminal-emulator)." >&2
        exit 1
    fi
}

run_services() {
    terminal_command "Estoque" "cd '$APP_DIR/stock' && dotnet run --project stock.csproj"
    terminal_command "Pagamento" "cd '$APP_DIR/payment' && dotnet run --project payment.csproj"
    terminal_command "Entrega" "cd '$APP_DIR/delivery' && dotnet run --project delivery.csproj"
    terminal_command "Pedidos" "cd '$APP_DIR/orders' && dotnet run --project orders.csproj"
    terminal_command "Consumidor de Promoções" "cd '$PROMOTIONS_DIR/consumer-promotions' && dotnet run --project consumer-promotions.csproj"
    terminal_command "Publisher de Promoções" "cd '$PROMOTIONS_DIR/publisher-promotions' && dotnet run --project publisher-promotions.csproj"
}

mode="all"
case "${1:-}" in
    ""|--all) mode="all" ;;
    --setup) mode="setup" ;;
    --build) mode="build" ;;
    --run) mode="run" ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Opção desconhecida: $1" >&2; usage >&2; exit 2 ;;
esac

case "$mode" in
    setup)
        setup_keys
        setup_rabbitmq
        ;;
    build)
        build_projects
        ;;
    run)
        run_services
        ;;
    all)
        setup_keys
        setup_rabbitmq
        build_projects
        run_services
        ;;
esac
