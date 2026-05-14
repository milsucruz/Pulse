#!/bin/bash
# infra/sqlserver/init-db.sh
# Aguarda o SQL Server estar pronto e executa o script de criação do banco.
# Roda no container sqlserver-init, uma única vez.

set -e

HOST="sqlserver"
PORT="1433"
SA_PASSWORD="${SA_PASSWORD:-NotifSystem@2024!}"
MAX_RETRIES=30
RETRY_INTERVAL=5

echo "⏳ Aguardando SQL Server em $HOST:$PORT..."

for i in $(seq 1 $MAX_RETRIES); do
  if /opt/mssql-tools18/bin/sqlcmd \
      -S "$HOST,$PORT" \
      -U sa \
      -P "$SA_PASSWORD" \
      -Q "SELECT 1" \
      -No -C > /dev/null 2>&1; then
    echo "✅ SQL Server disponível após $i tentativa(s)"
    break
  fi

  if [ "$i" -eq "$MAX_RETRIES" ]; then
    echo "❌ SQL Server não respondeu após $MAX_RETRIES tentativas. Abortando."
    exit 1
  fi

  echo "   Tentativa $i/$MAX_RETRIES — aguardando ${RETRY_INTERVAL}s..."
  sleep $RETRY_INTERVAL
done

echo "🔧 Executando script de criação do banco..."

/opt/mssql-tools18/bin/sqlcmd \
  -S "$HOST,$PORT" \
  -U sa \
  -P "$SA_PASSWORD" \
  -i /create-database.sql \
  -No -C

echo "✅ Banco NotificationSystem criado com sucesso."
