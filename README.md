# Лабораторная №3 — ASP.NET Core 9 + PostgreSQL

Вариант полностью на .NET: ASP.NET Core MVC, Razor Views, Npgsql и PostgreSQL. Проект открывается как `Lab3Accounting.sln` или напрямую как `Lab3Accounting.csproj`.

## Что реализовано

- три таблицы БД: `saldo`, `charges`, `payments`;
- WEB-интерфейс для заполнения таблиц;
- три отчетных модуля на хранимых функциях PostgreSQL;
- защита от повторных бизнес-записей на уровне приложения и БД;
- `created_at` / `updated_at` и отображение времени действий;
- контроль цепочки: исходящее сальдо текущего периода = входящее следующего;
- формула: `исходящее = входящее + начисления - платежи`;
- при изменении начислений и платежей пересчитываются только существующие строки `saldo`, новые месяцы автоматически не создаются;
- демонстрационная загрузка данных из задания через `ON CONFLICT DO NOTHING`.

## Запуск

1. Требуется .NET 9 SDK. .NET 8 остается LTS-веткой на момент подготовки проекта.
2. В корне проекта:

```bash
docker compose up -d
dotnet restore
dotnet run
```

3. Открыть `http://localhost:5080`. При старте приложение само применяет `db/schema.sql`, затем `db/seed.sql`, и пишет в консоль количество загруженных записей.

Если запускалось раньше и нужна полностью чистая БД:

```bash
docker compose down -v
docker compose up -d
dotnet run
```

Если в Docker уже есть старый volume, это самый надежный способ увидеть демо-данные заново.

Схема и demo-данные применяются приложением при каждом старте. `seed.sql` не создает дубликаты.

## Структура

- `Controllers/` — MVC-контроллеры
- `Services/LedgerService.cs` — операции с БД и бизнес-правила
- `db/schema.sql` — таблицы, индексы, триггеры и 3 хранимые функции
- `db/seed.sql` — стартовые данные из задания
- `Views/` — самостоятельный интерфейс
- `wwwroot/css/site.css` — внешний вид

## Ссылки

- `/ledger/saldo`
- `/ledger/charges`
- `/ledger/payments`
- `/reports/turnover?year=2017`
- `/reports/apartment?apartment=1&year=2017`
- `/reports/debtors?asOf=2017-10-01`

## Важное исправление запуска

Compose больше не задаёт фиксированный `container_name` и использует отдельный host-порт `55432`. Это предотвращает конфликт с оставшимся контейнером с именем `lab3-postgres` от старой версии проекта. Docker Compose обычно управляет контейнерами через имя проекта и сервиса, поэтому фиксированные имена здесь не нужны.

ASP.NET Core запускается без устаревшей настройки `устаревшую настройку MVC для value types`; в актуальном ASP.NET Core 8 соответствующая настройка MVC для nullable reference types называется `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`.
