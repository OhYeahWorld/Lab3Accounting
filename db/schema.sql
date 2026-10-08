BEGIN;

CREATE TABLE IF NOT EXISTS saldo (
    id BIGSERIAL PRIMARY KEY,
    apartment_number INTEGER NOT NULL CHECK (apartment_number > 0),
    period DATE NOT NULL CHECK (period = date_trunc('month', period)::date),
    opening_balance NUMERIC(14,2) NOT NULL,
    closing_balance NUMERIC(14,2) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_saldo_period UNIQUE (apartment_number, period)
);

CREATE TABLE IF NOT EXISTS charges (
    id BIGSERIAL PRIMARY KEY,
    apartment_number INTEGER NOT NULL CHECK (apartment_number > 0),
    period DATE NOT NULL CHECK (period = date_trunc('month', period)::date),
    amount NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    description VARCHAR(200) NOT NULL DEFAULT 'Начисление по квартире',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS payments (
    id BIGSERIAL PRIMARY KEY,
    apartment_number INTEGER NOT NULL CHECK (apartment_number > 0),
    period DATE NOT NULL CHECK (period = date_trunc('month', period)::date),
    payment_date DATE NOT NULL,
    payment_time TIME NOT NULL DEFAULT '12:00:00',
    amount NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    payment_reference VARCHAR(100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Модернизация старой базы: добавляем колонку времени платежа, если её нет.
ALTER TABLE payments ADD COLUMN IF NOT EXISTS payment_time TIME NOT NULL DEFAULT '12:00:00';

CREATE UNIQUE INDEX IF NOT EXISTS uq_charges_visible
    ON charges(apartment_number, period, amount);
DROP INDEX IF EXISTS uq_payments_visible;
CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_visible
    ON payments(apartment_number, period, payment_date, payment_time, amount);
CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_reference
    ON payments(payment_reference)
    WHERE payment_reference IS NOT NULL AND payment_reference <> '';

CREATE INDEX IF NOT EXISTS ix_saldo_apartment_period ON saldo(apartment_number, period);
CREATE INDEX IF NOT EXISTS ix_charges_apartment_period ON charges(apartment_number, period);
CREATE INDEX IF NOT EXISTS ix_payments_apartment_period ON payments(apartment_number, period);

CREATE OR REPLACE FUNCTION touch_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_saldo_touch ON saldo;
CREATE TRIGGER trg_saldo_touch
BEFORE UPDATE ON saldo
FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

DROP TRIGGER IF EXISTS trg_charges_touch ON charges;
CREATE TRIGGER trg_charges_touch
BEFORE UPDATE ON charges
FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

DROP TRIGGER IF EXISTS trg_payments_touch ON payments;
CREATE TRIGGER trg_payments_touch
BEFORE UPDATE ON payments
FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

CREATE OR REPLACE FUNCTION check_saldo_chain()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    prev_closing NUMERIC(14,2);
    next_opening NUMERIC(14,2);
BEGIN
    SELECT s.closing_balance INTO prev_closing
    FROM saldo s
    WHERE s.apartment_number = NEW.apartment_number
      AND s.period < NEW.period
      AND s.id <> COALESCE(NEW.id, -1)
    ORDER BY s.period DESC
    LIMIT 1;

    IF prev_closing IS NOT NULL AND NEW.opening_balance <> prev_closing THEN
        RAISE EXCEPTION 'Входящее сальдо квартиры % за % должно быть %', NEW.apartment_number, NEW.period, prev_closing;
    END IF;

    SELECT s.opening_balance INTO next_opening
    FROM saldo s
    WHERE s.apartment_number = NEW.apartment_number
      AND s.period > NEW.period
      AND s.id <> COALESCE(NEW.id, -1)
    ORDER BY s.period ASC
    LIMIT 1;

    IF next_opening IS NOT NULL AND NEW.closing_balance <> next_opening THEN
        RAISE EXCEPTION 'Исходящее сальдо квартиры % за % должно быть %', NEW.apartment_number, NEW.period, next_opening;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_check_saldo_chain ON saldo;
CREATE CONSTRAINT TRIGGER trg_check_saldo_chain
AFTER INSERT OR UPDATE ON saldo
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION check_saldo_chain();

CREATE OR REPLACE FUNCTION check_saldo_formula()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    charges_total NUMERIC(14,2);
    payments_total NUMERIC(14,2);
    expected NUMERIC(14,2);
BEGIN
    SELECT COALESCE(SUM(c.amount),0) INTO charges_total FROM charges c WHERE c.apartment_number=NEW.apartment_number AND c.period=NEW.period;
    SELECT COALESCE(SUM(p.amount),0) INTO payments_total FROM payments p WHERE p.apartment_number=NEW.apartment_number AND p.period=NEW.period;
    expected := NEW.opening_balance + charges_total - payments_total;
    IF NEW.closing_balance <> expected THEN
        RAISE EXCEPTION 'Исходящее сальдо квартиры % за % должно быть %, рассчитано из входящего, начислений и платежей', NEW.apartment_number, NEW.period, expected;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_check_saldo_formula ON saldo;
CREATE CONSTRAINT TRIGGER trg_check_saldo_formula
AFTER INSERT OR UPDATE ON saldo
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION check_saldo_formula();

DROP FUNCTION IF EXISTS sp_turnover_statement(INTEGER);
DROP FUNCTION IF EXISTS sp_apartment_statement(INTEGER, INTEGER);
DROP FUNCTION IF EXISTS sp_debtors_summary(DATE);

CREATE OR REPLACE FUNCTION sp_turnover_statement(p_year INTEGER)
RETURNS TABLE (
    apartment_number INTEGER,
    year_opening NUMERIC(14,2),
    month_no INTEGER,
    month_start DATE,
    opening_balance NUMERIC(14,2),
    charges_total NUMERIC(14,2),
    payments_total NUMERIC(14,2),
    closing_balance NUMERIC(14,2),
    action_at TIMESTAMPTZ,
    calc_opening NUMERIC(14,2),
    calc_closing NUMERIC(14,2)
)
LANGUAGE sql
AS $$
WITH apartments AS (
    SELECT apartment_number FROM saldo WHERE EXTRACT(YEAR FROM period) = p_year
    UNION SELECT apartment_number FROM charges WHERE EXTRACT(YEAR FROM period) = p_year
    UNION SELECT apartment_number FROM payments WHERE EXTRACT(YEAR FROM period) = p_year
),
months AS (
    SELECT generate_series(1,12)::INT AS month_no
),
rows AS (
    SELECT a.apartment_number,
           m.month_no,
           make_date(p_year,m.month_no,1) AS month_start,
           s.opening_balance,
           COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=a.apartment_number AND c.period=make_date(p_year,m.month_no,1)),0)::NUMERIC(14,2) AS charges_total,
           COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=a.apartment_number AND p.period=make_date(p_year,m.month_no,1)),0)::NUMERIC(14,2) AS payments_total,
           s.closing_balance,
           NULLIF(GREATEST(
               COALESCE(s.updated_at,'-infinity'::timestamptz),
               COALESCE((SELECT MAX(GREATEST(c.created_at,c.updated_at)) FROM charges c WHERE c.apartment_number=a.apartment_number AND c.period=make_date(p_year,m.month_no,1)),'-infinity'::timestamptz),
               COALESCE((SELECT MAX(GREATEST(p.created_at,p.updated_at,payment_time::timestamp)) FROM payments p WHERE p.apartment_number=a.apartment_number AND p.period=make_date(p_year,m.month_no,1)),'-infinity'::timestamptz)
           ),'-infinity'::timestamptz) AS action_at,
           (SELECT s0.opening_balance FROM saldo s0
             WHERE s0.apartment_number=a.apartment_number AND s0.period < make_date(p_year,1,1)
             ORDER BY s0.period DESC LIMIT 1) AS prev_year_opening,
           (SELECT s1.closing_balance + COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=a.apartment_number AND c.period > s1.period AND c.period < make_date(p_year,m.month_no,1)),0)
                          - COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=a.apartment_number AND p.period > s1.period AND p.period < make_date(p_year,m.month_no,1)),0)
             FROM saldo s1
             WHERE s1.apartment_number=a.apartment_number AND s1.period < make_date(p_year,m.month_no,1)
             ORDER BY s1.period DESC LIMIT 1) AS chain_opening,
           (SELECT s2.opening_balance - COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=a.apartment_number AND c.period > make_date(p_year,m.month_no,1) AND c.period < s2.period),0)
                          + COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=a.apartment_number AND p.period > make_date(p_year,m.month_no,1) AND p.period < s2.period),0)
             FROM saldo s2
             WHERE s2.apartment_number=a.apartment_number AND s2.period > make_date(p_year,m.month_no,1)
             ORDER BY s2.period ASC LIMIT 1) AS chain_closing_next,
    FROM apartments a CROSS JOIN months m
    LEFT JOIN saldo s ON s.apartment_number=a.apartment_number AND s.period=make_date(p_year,m.month_no,1)
),
calc AS (
    -- Расчет входящего и исходящего сальдо для КАЖДОЙ ячейки независимо от наличия строки в saldo:
    -- входящее = первое входящее за год + накопленные (начисления - платежи) предыдущих месяцев;
    -- исходящее = входящее + начисления текущего месяца - платежи текущего месяца.
    SELECT r.apartment_number,
           r.month_no,
           COALESCE(MIN(r.opening_balance) OVER (PARTITION BY r.apartment_number),
                    MAX(r.prev_year_opening) OVER (PARTITION BY r.apartment_number)) AS base_opening,
           MIN(r.month_no) FILTER (WHERE r.opening_balance IS NOT NULL) OVER (PARTITION BY r.apartment_number) AS first_saldo_month,
           SUM(r.charges_total - r.payments_total) OVER (
               PARTITION BY r.apartment_number ORDER BY r.month_no
               ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
           ) AS total_before,
           r.charges_total,
           r.payments_total,
           r.opening_balance,
           r.closing_balance,
           r.action_at,
           r.month_start,
           r.chain_opening,
           r.chain_closing_next
    FROM rows r
)
SELECT apartment_number,
       MIN(opening_balance) OVER (PARTITION BY apartment_number) AS year_opening,
       month_no,
       month_start,
       opening_balance,
       charges_total,
       payments_total,
       closing_balance,
       action_at,
       ROUND(COALESCE(
           opening_balance,
           chain_opening,
           chain_closing_next - (charges_total - payments_total),
           base_opening + CASE WHEN month_no >= COALESCE(first_saldo_month, 13)
                               THEN total_before
                               ELSE total_before - (charges_total - payments_total) END,
           0), 2)::NUMERIC(14,2) AS calc_opening,
       ROUND(COALESCE(
           closing_balance,
           COALESCE(opening_balance, chain_opening,
                    chain_closing_next - (charges_total - payments_total),
                    base_opening + CASE WHEN month_no >= COALESCE(first_saldo_month, 13)
                                        THEN total_before
                                        ELSE total_before - (charges_total - payments_total) END,
                    0)
           + charges_total - payments_total, 2)::NUMERIC(14,2) AS calc_closing
FROM calc
ORDER BY apartment_number, month_no;
$$;

CREATE OR REPLACE FUNCTION sp_apartment_statement(p_apartment INTEGER, p_year INTEGER)
RETURNS TABLE (
    month_no INTEGER,
    month_start DATE,
    opening_balance NUMERIC(14,2),
    charges_total NUMERIC(14,2),
    payments_total NUMERIC(14,2),
    closing_balance NUMERIC(14,2),
    action_at TIMESTAMPTZ,
    calc_opening NUMERIC(14,2),
    calc_closing NUMERIC(14,2)
)
LANGUAGE sql
AS $$
WITH base AS (
SELECT gs::INT,
       make_date(p_year,gs::INT,1),
       s.opening_balance,
       COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=p_apartment AND c.period=make_date(p_year,gs::INT,1)),0)::NUMERIC(14,2),
       COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=p_apartment AND p.period=make_date(p_year,gs::INT,1)),0)::NUMERIC(14,2),
       s.closing_balance,
       NULLIF(GREATEST(
           COALESCE(s.updated_at,'-infinity'::timestamptz),
           COALESCE((SELECT MAX(GREATEST(c.created_at,c.updated_at)) FROM charges c WHERE c.apartment_number=p_apartment AND c.period=make_date(p_year,gs::INT,1)),'-infinity'::timestamptz),
           COALESCE((SELECT MAX(GREATEST(p.created_at,p.updated_at)) FROM payments p WHERE p.apartment_number=p_apartment AND p.period=make_date(p_year,gs::INT,1)),'-infinity'::timestamptz)
       ),'-infinity'::timestamptz),
       (SELECT s0.opening_balance FROM saldo s0
         WHERE s0.apartment_number=p_apartment AND s0.period < make_date(p_year,1,1)
         ORDER BY s0.period DESC LIMIT 1),
       (SELECT s1.closing_balance + COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=p_apartment AND c.period > s1.period AND c.period < make_date(p_year,gs::INT,1)),0)
                        - COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=p_apartment AND p.period > s1.period AND p.period < make_date(p_year,gs::INT,1)),0)
        FROM saldo s1
        WHERE s1.apartment_number=p_apartment AND s1.period < make_date(p_year,gs::INT,1)
        ORDER BY s1.period DESC LIMIT 1),
       (SELECT s2.opening_balance - COALESCE((SELECT SUM(c.amount) FROM charges c WHERE c.apartment_number=p_apartment AND c.period > make_date(p_year,gs::INT,1) AND c.period < s2.period),0)
                        + COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.apartment_number=p_apartment AND p.period > make_date(p_year,gs::INT,1) AND p.period < s2.period),0)
        FROM saldo s2
        WHERE s2.apartment_number=p_apartment AND s2.period > make_date(p_year,gs::INT,1)
        ORDER BY s2.period ASC LIMIT 1)
FROM generate_series(1,12) gs
LEFT JOIN saldo s ON s.apartment_number=p_apartment AND s.period=make_date(p_year,gs::INT,1)
),
calc AS (
    -- Расчет входящего и исходящего сальдо для каждого месяца ленты квартиры.
    SELECT b.*,
           b.column8 AS prev_year_opening,
           b.column9 AS chain_opening,
           b.column10 AS chain_closing_next,
           COALESCE(MIN(b.opening_balance) OVER (), MAX(b.column8) OVER ()) AS base_opening,
           MIN(b.month_no) FILTER (WHERE b.opening_balance IS NOT NULL) OVER () AS first_saldo_month,
           SUM(b.charges_total - b.payments_total) OVER (ORDER BY b.month_no ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS total_before
    FROM base b
)
SELECT month_no, month_start, opening_balance, charges_total, payments_total, closing_balance, action_at,
       ROUND(COALESCE(
           opening_balance,
           chain_opening,
           chain_closing_next - (charges_total - payments_total),
           base_opening + CASE WHEN month_no >= COALESCE(first_saldo_month, 13)
                               THEN total_before
                               ELSE total_before - (charges_total - payments_total) END,
           0), 2)::NUMERIC(14,2),
       ROUND(COALESCE(
           closing_balance,
           COALESCE(opening_balance, chain_opening,
                    chain_closing_next - (charges_total - payments_total),
                    base_opening + CASE WHEN month_no >= COALESCE(first_saldo_month, 13)
                                        THEN total_before
                                        ELSE total_before - (charges_total - payments_total) END,
                    0)
           + charges_total - payments_total, 2)::NUMERIC(14,2)
FROM calc
ORDER BY month_no;
$$;

CREATE OR REPLACE FUNCTION sp_debtors_summary(p_as_of DATE)
RETURNS TABLE (
    apartment_number INTEGER,
    last_month_charge NUMERIC(14,2),
    balance NUMERIC(14,2),
    one_month NUMERIC(14,2),
    two_months NUMERIC(14,2),
    three_months NUMERIC(14,2),
    over_three_months NUMERIC(14,2),
    debt_months NUMERIC(12,2),
    category VARCHAR(20),
    action_at TIMESTAMPTZ
)
LANGUAGE sql
AS $$
WITH lim AS (
    SELECT date_trunc('month', p_as_of)::DATE AS month_limit
),
last_saldo AS (
    SELECT DISTINCT ON (s.apartment_number) s.apartment_number,s.closing_balance,s.updated_at
    FROM saldo s,lim
    WHERE s.period < lim.month_limit
    ORDER BY s.apartment_number,s.period DESC
),
last_charge AS (
    SELECT DISTINCT ON (c.apartment_number) c.apartment_number,c.period,SUM(c.amount) OVER (PARTITION BY c.apartment_number,c.period) AS amount
    FROM charges c,lim
    WHERE c.period < lim.month_limit
    ORDER BY c.apartment_number,c.period DESC
),
base AS (
    SELECT s.apartment_number,lc.amount,s.closing_balance,
           lc.period AS charge_period,
           GREATEST(s.updated_at,COALESCE((SELECT MAX(GREATEST(c.created_at,c.updated_at)) FROM charges c WHERE c.apartment_number=s.apartment_number),'epoch'::timestamptz),COALESCE((SELECT MAX(GREATEST(p.created_at,p.updated_at)) FROM payments p WHERE p.apartment_number=s.apartment_number),'epoch'::timestamptz)) AS action_at
    FROM last_saldo s
    JOIN last_charge lc USING (apartment_number)
)
SELECT apartment_number,
       amount,
       closing_balance,
       CASE WHEN CEIL(closing_balance/NULLIF(amount,0))=1 THEN closing_balance END,
       CASE WHEN CEIL(closing_balance/NULLIF(amount,0))=2 THEN closing_balance END,
       CASE WHEN CEIL(closing_balance/NULLIF(amount,0))=3 THEN closing_balance END,
       CASE WHEN CEIL(closing_balance/NULLIF(amount,0))>3 THEN closing_balance END,
       CEIL(closing_balance/NULLIF(amount,0))::NUMERIC(12,2),
       CASE
           WHEN CEIL(closing_balance/NULLIF(amount,0))=1 THEN '1 месяц'
           WHEN CEIL(closing_balance/NULLIF(amount,0))=2 THEN '2 месяца'
           WHEN CEIL(closing_balance/NULLIF(amount,0))=3 THEN '3 месяца'
           ELSE 'Свыше 3 месяцев'
       END::VARCHAR(20),
       action_at
FROM base
WHERE closing_balance > 0 AND amount > 0
ORDER BY apartment_number;
$$;

COMMIT;
