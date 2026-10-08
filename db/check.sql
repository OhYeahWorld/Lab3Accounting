-- Быстрая проверка требований лабораторной после запуска приложения.
SELECT 'saldo' AS table_name, COUNT(*) AS row_count FROM saldo
UNION ALL
SELECT 'charges', COUNT(*) FROM charges
UNION ALL
SELECT 'payments', COUNT(*) FROM payments;

-- Ошибок в цепочке быть не должно.
SELECT s1.apartment_number, s1.period, s1.closing_balance, s2.period AS next_period, s2.opening_balance
FROM saldo s1
JOIN saldo s2 ON s2.apartment_number=s1.apartment_number
            AND s2.period=(SELECT MIN(s3.period) FROM saldo s3 WHERE s3.apartment_number=s1.apartment_number AND s3.period>s1.period)
WHERE s1.closing_balance <> s2.opening_balance;

-- Ошибок формулы быть не должно.
SELECT s.apartment_number, s.period, s.opening_balance, s.closing_balance,
       s.opening_balance + COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=s.apartment_number AND c.period=s.period),0)
       - COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=s.apartment_number AND p.period=s.period),0) AS expected
FROM saldo s
WHERE s.closing_balance <> s.opening_balance
      + COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=s.apartment_number AND c.period=s.period),0)
      - COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=s.apartment_number AND p.period=s.period),0);
