#!/usr/bin/env bash
set -euo pipefail

if grep -n 'SuppressImplicitRequiredAttributeForValueTypes' Program.cs >/dev/null 2>&1; then echo 'Ошибка: в Program.cs осталась несовместимая настройка'; exit 1; fi
if grep -n '5433' appsettings.json docker-compose.yml >/dev/null 2>&1; then echo 'Ошибка: в конфигурации остался старый порт'; exit 1; fi
if grep -n '^ *container_name:' docker-compose.yml >/dev/null 2>&1; then echo 'Ошибка: осталось фиксированное имя контейнера'; exit 1; fi
test -f Views/_ViewImports.cshtml
test -f Views/_ViewStart.cshtml
test -f Views/Home/Index.cshtml
test -f Views/Ledger/Saldo.cshtml
test -f Views/Ledger/Charges.cshtml
test -f Views/Ledger/Payments.cshtml
test -f Views/Reports/Turnover.cshtml
test -f Views/Reports/Apartment.cshtml
test -f Views/Reports/Debtors.cshtml
printf 'Static project checks: OK\n'
