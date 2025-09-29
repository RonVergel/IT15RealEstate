#!/usr/bin/env bash
set -euo pipefail

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

# 🔹 Run EF migrations
echo "Applying EF Core migrations..."
dotnet ef database update --no-build --project RealEstateCRM/RealEstateCRM.csproj

echo "Starting application..."
exec dotnet RealEstateCRM.dll

