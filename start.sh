#!/usr/bin/env bash
set -euo pipefail

echo '1/3  Запускаю PostgreSQL...'
docker compose up -d

echo '2/3  Восстанавливаю зависимости...'
dotnet restore

echo '3/3  Запускаю ASP.NET Core...'
dotnet run
