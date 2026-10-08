$ErrorActionPreference = 'Stop'
Write-Host '1/3  Запускаю PostgreSQL...'
docker compose up -d
Write-Host '2/3  Восстанавливаю зависимости...'
dotnet restore
Write-Host '3/3  Запускаю ASP.NET Core...'
dotnet run
