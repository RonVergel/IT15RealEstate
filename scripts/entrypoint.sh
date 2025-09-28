#!/usr/bin/env bash
set -euo pipefail

# If DATABASE_URL is present, try to derive DB host/port
if [ -n "${DATABASE_URL:-}" ]; then
  DB_HOST=$(echo "${DATABASE_URL}" | sed -E 's#.*@([^:/]+).*#\1#')
  DB_PORT=$(echo "${DATABASE_URL}" | sed -E 's#.*:([0-9]+)/.*#\1#')
fi

# Fallbacks
DB_HOST=${DB_HOST:-${DB_HOST:-db}}
DB_PORT=${DB_PORT:-${DB_PORT:-5432}}
DB_USER=${POSTGRES_USER:-${DB_USER:-postgres}}

echo "Waiting for Postgres at ${DB_HOST}:${DB_PORT} (user=${DB_USER})..."
retries=0
until pg_isready -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" > /dev/null 2>&1; do
  retries=$((retries+1))
  if [ $retries -ge 60 ]; then
    echo "Postgres did not become available after $retries tries, exiting."
    exit 1
  fi
  sleep 2
done

echo "Postgres is ready."

# Optionally run EF migrations (comment/uncomment as desired)
# echo "Applying EF Core migrations..."
# dotnet ef database update --no-build

echo "Starting app..."
exec dotnet RealEstateCRM.dll