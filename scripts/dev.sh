#!/bin/bash
# scripts/dev.sh — comandos de conveniência para o ambiente de desenvolvimento local
# Uso: ./scripts/dev.sh [up|down|reset|status|logs|rabbit|sql]

set -e

COMPOSE_FILE="docker-compose.yml"
PROJECT_NAME="notification"

case "${1:-help}" in

  up)
    echo "🚀 Subindo o ambiente de desenvolvimento..."
    cp -n .env.example .env 2>/dev/null || true
    docker compose -p $PROJECT_NAME -f $COMPOSE_FILE up -d --wait
    echo ""
    echo "✅ Ambiente pronto!"
    echo ""
    echo "  RabbitMQ Management UI → http://localhost:15672"
    echo "  Usuário: admin  |  Senha: admin123"
    echo ""
    echo "  SQL Server → localhost:1433"
    echo "  Usuário: sa  |  Senha: NotifSystem@2024!"
    echo ""
    echo "  Connection string:"
    echo "  Server=localhost,1433;Database=NotificationSystem;User Id=sa;Password=NotifSystem@2024!;TrustServerCertificate=True"
    ;;

  down)
    echo "🛑 Parando containers..."
    docker compose -p $PROJECT_NAME -f $COMPOSE_FILE down
    echo "✅ Containers parados. Volumes preservados."
    ;;

  reset)
    echo "⚠️  Isso vai apagar todos os dados (volumes). Continuar? [y/N]"
    read -r confirm
    if [[ "$confirm" =~ ^[Yy]$ ]]; then
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE down -v --remove-orphans
      echo "✅ Ambiente resetado. Rode './scripts/dev.sh up' para reiniciar."
    else
      echo "Operação cancelada."
    fi
    ;;

  status)
    echo "📊 Status dos containers:"
    docker compose -p $PROJECT_NAME -f $COMPOSE_FILE ps
    ;;

  logs)
    SERVICE="${2:-}"
    if [ -n "$SERVICE" ]; then
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE logs -f "$SERVICE"
    else
      docker compose -p $PROJECT_NAME -f $COMPOSE_FILE logs -f
    fi
    ;;

  rabbit)
    echo "🐰 Abrindo RabbitMQ Management UI..."
    # Verifica se o serviço está healthy antes de abrir
    if docker inspect notification-rabbitmq --format='{{.State.Health.Status}}' 2>/dev/null | grep -q "healthy"; then
      open "http://localhost:15672" 2>/dev/null || xdg-open "http://localhost:15672" 2>/dev/null || echo "Abra: http://localhost:15672"
    else
      echo "⚠️  RabbitMQ ainda não está healthy. Aguarde e tente novamente."
    fi
    ;;

  sql)
    echo "🗄️  Conectando ao SQL Server via sqlcmd..."
    docker exec -it notification-sqlserver \
      /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U sa \
      -P "NotifSystem@2024!" \
      -d NotificationSystem \
      -No -C
    ;;

  help|*)
    echo ""
    echo "Uso: ./scripts/dev.sh <comando>"
    echo ""
    echo "Comandos:"
    echo "  up       Sobe RabbitMQ + SQL Server e aguarda healthchecks"
    echo "  down     Para os containers (mantém volumes/dados)"
    echo "  reset    Para e apaga todos os dados (volumes)"
    echo "  status   Mostra o status dos containers"
    echo "  logs     Segue os logs (opcional: logs rabbitmq | logs sqlserver)"
    echo "  rabbit   Abre o RabbitMQ Management UI no browser"
    echo "  sql      Abre o sqlcmd interativo no container SQL Server"
    echo ""
    ;;
esac
