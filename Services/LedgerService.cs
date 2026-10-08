using Lab3Accounting.Models;
using Lab3Accounting.ViewModels;
using Npgsql;
using NpgsqlTypes;

namespace Lab3Accounting.Services;

public sealed class LedgerService
{
    private readonly string _connectionString;

    public LedgerService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");
    }

    public async Task<List<Saldo>> GetSaldoAsync(int? apartment = null)
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, apartment_number, period, opening_balance, closing_balance, created_at, updated_at
            FROM saldo
            WHERE CAST(@apartment AS integer) IS NULL OR apartment_number = CAST(@apartment AS integer)
            ORDER BY apartment_number, period;", db);
        cmd.Parameters.Add("apartment", NpgsqlDbType.Integer).Value = apartment.HasValue ? apartment.Value : DBNull.Value;
        var result = new List<Saldo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new Saldo
        {
            Id = reader.GetInt64(0), ApartmentNumber = reader.GetInt32(1), Period = reader.GetFieldValue<DateOnly>(2),
            OpeningBalance = reader.GetDecimal(3), ClosingBalance = reader.GetDecimal(4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
        });
        return result;
    }

    public async Task<List<Charge>> GetChargesAsync(int? apartment = null)
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, apartment_number, period, amount, description, created_at, updated_at
            FROM charges
            WHERE CAST(@apartment AS integer) IS NULL OR apartment_number = CAST(@apartment AS integer)
            ORDER BY period DESC, apartment_number, id DESC;", db);
        cmd.Parameters.Add("apartment", NpgsqlDbType.Integer).Value = apartment.HasValue ? apartment.Value : DBNull.Value;
        var result = new List<Charge>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new Charge
        {
            Id = reader.GetInt64(0), ApartmentNumber = reader.GetInt32(1), Period = reader.GetFieldValue<DateOnly>(2),
            Amount = reader.GetDecimal(3), Description = reader.GetString(4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
        });
        return result;
    }

    public async Task<List<Payment>> GetPaymentsAsync(int? apartment = null)
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, apartment_number, period, payment_date, amount, payment_reference, created_at, updated_at
            FROM payments
            WHERE CAST(@apartment AS integer) IS NULL OR apartment_number = CAST(@apartment AS integer)
            ORDER BY payment_date DESC, apartment_number, id DESC;", db);
        cmd.Parameters.Add("apartment", NpgsqlDbType.Integer).Value = apartment.HasValue ? apartment.Value : DBNull.Value;
        var result = new List<Payment>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new Payment
        {
            Id = reader.GetInt64(0), ApartmentNumber = reader.GetInt32(1), Period = reader.GetFieldValue<DateOnly>(2),
            PaymentDate = reader.GetFieldValue<DateOnly>(3), Amount = reader.GetDecimal(4),
            PaymentReference = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(6), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(7)
        });
        return result;
    }

    public async Task SaveSaldoAsync(SaldoForm form)
    {
        ValidateApartmentAndPeriod(form.ApartmentNumber, form.Period);
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            var existing = await FindSaldoByPeriodAsync(db, tx, form.ApartmentNumber, form.Period, form.Id);
            if (existing is not null) throw new InvalidOperationException("Сальдо за эту квартиру и период уже существует.");

            var previous = await GetBoundarySaldoAsync(db, tx, form.ApartmentNumber, form.Period, previous: true);
            if (previous is not null && form.OpeningBalance != previous.Value.Closing)
                throw new InvalidOperationException($"Входящее сальдо должно быть {previous.Value.Closing:N2}: это исходящее сальдо предыдущего периода.");

            var expected = form.OpeningBalance + await SumChargesAsync(db, tx, form.ApartmentNumber, form.Period) - await SumPaymentsAsync(db, tx, form.ApartmentNumber, form.Period);
            var next = await GetBoundarySaldoAsync(db, tx, form.ApartmentNumber, form.Period, previous: false);
            if (next is not null && expected != next.Value.Opening)
                throw new InvalidOperationException($"Расчетное исходящее сальдо {expected:N2} не совпадает с входящим следующего периода {next.Value.Opening:N2}.");

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO saldo(apartment_number, period, opening_balance, closing_balance)
                VALUES (@apartment,@period,@opening,@closing);", db, tx);
            Add(cmd, "apartment", form.ApartmentNumber);
            Add(cmd, "period", form.Period);
            Add(cmd, "opening", form.OpeningBalance);
            Add(cmd, "closing", expected);
            await cmd.ExecuteNonQueryAsync();
            await RebuildSaldoChainAsync(db, tx, form.ApartmentNumber);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateSaldoAsync(SaldoForm form)
    {
        if (form.Id is null) throw new ArgumentException("Не указан идентификатор записи.");
        ValidateApartmentAndPeriod(form.ApartmentNumber, form.Period);
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            var current = await FindSaldoByIdAsync(db, tx, form.Id.Value);
            if (current is null) throw new KeyNotFoundException("Запись сальдо не найдена.");
            var duplicate = await FindSaldoByPeriodAsync(db, tx, form.ApartmentNumber, form.Period, form.Id);
            if (duplicate is not null) throw new InvalidOperationException("Другая запись уже содержит такую квартиру и период.");

            var previous = await GetBoundarySaldoAsync(db, tx, form.ApartmentNumber, form.Period, previous: true, form.Id);
            var opening = previous?.Closing ?? form.OpeningBalance;
            if (previous is not null && form.OpeningBalance != previous.Value.Closing)
                throw new InvalidOperationException($"Входящее сальдо должно быть {previous.Value.Closing:N2}: это исходящее сальдо предыдущего периода.");

            var expected = opening + await SumChargesAsync(db, tx, form.ApartmentNumber, form.Period) - await SumPaymentsAsync(db, tx, form.ApartmentNumber, form.Period);
            var next = await GetBoundarySaldoAsync(db, tx, form.ApartmentNumber, form.Period, previous: false, form.Id);
            if (next is not null && expected != next.Value.Opening)
                throw new InvalidOperationException($"Расчетное исходящее сальдо {expected:N2} не совпадает с входящим следующего периода {next.Value.Opening:N2}.");

            await using var cmd = new NpgsqlCommand(@"
                UPDATE saldo SET apartment_number=@apartment, period=@period, opening_balance=@opening, closing_balance=@closing
                WHERE id=@id;", db, tx);
            Add(cmd, "id", form.Id.Value); Add(cmd, "apartment", form.ApartmentNumber); Add(cmd, "period", form.Period);
            Add(cmd, "opening", opening); Add(cmd, "closing", expected);
            await cmd.ExecuteNonQueryAsync();
            await RebuildSaldoChainAsync(db, tx, form.ApartmentNumber);
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task SaveChargeAsync(ChargeForm form)
    {
        ValidateApartmentAndPeriod(form.ApartmentNumber, form.Period);
        if (form.Amount <= 0) throw new InvalidOperationException("Сумма начисления должна быть больше нуля.");
        if (string.IsNullOrWhiteSpace(form.Description)) form.Description = "Начисление по квартире";
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            await EnsureChargeUniqueAsync(db, tx, form);
            if (form.Id is null)
            {
                await using var cmd = new NpgsqlCommand(@"INSERT INTO charges(apartment_number,period,amount,description) VALUES(@apartment,@period,@amount,@description);", db, tx);
                Add(cmd,"apartment",form.ApartmentNumber); Add(cmd,"period",form.Period); Add(cmd,"amount",form.Amount); Add(cmd,"description",form.Description);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                await using var cmd = new NpgsqlCommand(@"UPDATE charges SET apartment_number=@apartment,period=@period,amount=@amount,description=@description WHERE id=@id;", db, tx);
                Add(cmd,"id",form.Id.Value); Add(cmd,"apartment",form.ApartmentNumber); Add(cmd,"period",form.Period); Add(cmd,"amount",form.Amount); Add(cmd,"description",form.Description);
                if (await cmd.ExecuteNonQueryAsync() == 0) throw new KeyNotFoundException("Начисление не найдено.");
            }
            await RebuildSaldoChainAsync(db, tx, form.ApartmentNumber);
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") { await tx.RollbackAsync(); throw new InvalidOperationException("Такое начисление уже есть в базе.", ex); }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task SavePaymentAsync(PaymentForm form)
    {
        ValidateApartmentAndPeriod(form.ApartmentNumber, form.Period);
        if (form.Amount <= 0) throw new InvalidOperationException("Сумма платежа должна быть больше нуля.");
        if (form.PaymentDate < form.Period || form.PaymentDate >= form.Period.AddMonths(1))
            throw new InvalidOperationException("Дата платежа должна относиться к выбранному месяцу.");
        if (string.IsNullOrWhiteSpace(form.PaymentReference)) form.PaymentReference = null;
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            await EnsurePaymentUniqueAsync(db, tx, form);
            if (form.Id is null)
            {
                await using var cmd = new NpgsqlCommand(@"INSERT INTO payments(apartment_number,period,payment_date,amount,payment_reference) VALUES(@apartment,@period,@paymentDate,@amount,@reference);", db, tx);
                Add(cmd,"apartment",form.ApartmentNumber); Add(cmd,"period",form.Period); Add(cmd,"paymentDate",form.PaymentDate); Add(cmd,"amount",form.Amount); Add(cmd,"reference",(object?)form.PaymentReference ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                await using var cmd = new NpgsqlCommand(@"UPDATE payments SET apartment_number=@apartment,period=@period,payment_date=@paymentDate,amount=@amount,payment_reference=@reference WHERE id=@id;", db, tx);
                Add(cmd,"id",form.Id.Value); Add(cmd,"apartment",form.ApartmentNumber); Add(cmd,"period",form.Period); Add(cmd,"paymentDate",form.PaymentDate); Add(cmd,"amount",form.Amount); Add(cmd,"reference",(object?)form.PaymentReference ?? DBNull.Value);
                if (await cmd.ExecuteNonQueryAsync() == 0) throw new KeyNotFoundException("Платеж не найден.");
            }
            await RebuildSaldoChainAsync(db, tx, form.ApartmentNumber);
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") { await tx.RollbackAsync(); throw new InvalidOperationException("Такой платеж уже есть в базе.", ex); }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task DeleteAsync(string table, long id)
    {
        var allowed = table is "saldo" or "charges" or "payments";
        if (!allowed) throw new ArgumentException("Недопустимая таблица.");
        await using var db = await OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        try
        {
            int apartment;
            if (table == "saldo")
            {
                apartment = await GetApartmentByIdAsync(db, tx, table, id);
                await ExecuteDeleteAsync(db, tx, table, id);
                await RebuildSaldoChainAsync(db, tx, apartment);
            }
            else
            {
                apartment = await GetApartmentByIdAsync(db, tx, table, id);
                await ExecuteDeleteAsync(db, tx, table, id);
                await RebuildSaldoChainAsync(db, tx, apartment);
            }
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task<HomeViewModel> GetHomeAsync()
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT (SELECT COUNT(*)::int FROM saldo), (SELECT COUNT(*)::int FROM charges), (SELECT COUNT(*)::int FROM payments),
                   (SELECT COUNT(DISTINCT apartment_number)::int FROM (
                        SELECT apartment_number FROM saldo UNION SELECT apartment_number FROM charges UNION SELECT apartment_number FROM payments
                   ) q),
                   (SELECT COALESCE(SUM(closing_balance),0) FROM saldo s WHERE s.period=(SELECT MAX(period) FROM saldo z WHERE z.apartment_number=s.apartment_number)),
                   (SELECT MAX(t) FROM (SELECT MAX(created_at) t FROM saldo UNION SELECT MAX(updated_at) FROM saldo UNION SELECT MAX(created_at) FROM charges UNION SELECT MAX(updated_at) FROM charges UNION SELECT MAX(created_at) FROM payments UNION SELECT MAX(updated_at) FROM payments) x);", db);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new HomeViewModel
        {
            SaldoCount = r.GetInt32(0), ChargesCount = r.GetInt32(1), PaymentsCount = r.GetInt32(2), ApartmentCount = r.GetInt32(3),
            TotalDebtorBalance = r.GetDecimal(4), LastActionAt = r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5)
        };
    }

    public async Task<List<TurnoverRow>> GetTurnoverAsync(int year)
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM sp_turnover_statement(@year);", db);
        Add(cmd,"year",year);
        var list = new List<TurnoverRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(new TurnoverRow
        {
            ApartmentNumber=r.GetInt32(0), YearOpening=r.IsDBNull(1)?null:r.GetDecimal(1), MonthNo=r.GetInt32(2), MonthStart=r.GetFieldValue<DateOnly>(3),
            OpeningBalance=r.IsDBNull(4)?null:r.GetDecimal(4), ChargesTotal=r.GetDecimal(5), PaymentsTotal=r.GetDecimal(6),
            ClosingBalance=r.IsDBNull(7)?null:r.GetDecimal(7), ActionAt=r.IsDBNull(8)?null:r.GetFieldValue<DateTimeOffset>(8)
        });
        return list;
    }

    public async Task<List<ApartmentRow>> GetApartmentReportAsync(int apartment, int year)
    {
        await using var db = await OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM sp_apartment_statement(@apartment,@year);", db);
        Add(cmd,"apartment",apartment); Add(cmd,"year",year);
        var list=new List<ApartmentRow>();
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(new ApartmentRow{MonthNo=r.GetInt32(0),MonthStart=r.GetFieldValue<DateOnly>(1),OpeningBalance=r.IsDBNull(2)?null:r.GetDecimal(2),ChargesTotal=r.GetDecimal(3),PaymentsTotal=r.GetDecimal(4),ClosingBalance=r.IsDBNull(5)?null:r.GetDecimal(5),ActionAt=r.IsDBNull(6)?null:r.GetFieldValue<DateTimeOffset>(6)});
        return list;
    }

    public async Task<List<DebtorRow>> GetDebtorsAsync(DateOnly asOf)
    {
        await using var db=await OpenAsync();
        await using var cmd=new NpgsqlCommand("SELECT * FROM sp_debtors_summary(@asOf);",db);
        Add(cmd,"asOf",asOf);
        var list=new List<DebtorRow>();
        await using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(new DebtorRow{ApartmentNumber=r.GetInt32(0),LastMonthCharge=r.GetDecimal(1),Balance=r.GetDecimal(2),OneMonth=r.IsDBNull(3)?null:r.GetDecimal(3),TwoMonths=r.IsDBNull(4)?null:r.GetDecimal(4),ThreeMonths=r.IsDBNull(5)?null:r.GetDecimal(5),OverThreeMonths=r.IsDBNull(6)?null:r.GetDecimal(6),DebtMonths=r.GetDecimal(7),Category=r.GetString(8),ActionAt=r.IsDBNull(9)?null:r.GetFieldValue<DateTimeOffset>(9)});
        return list;
    }

    public async Task<List<int>> GetApartmentNumbersAsync()
    {
        await using var db=await OpenAsync();
        await using var cmd=new NpgsqlCommand(@"SELECT apartment_number FROM (SELECT apartment_number FROM saldo UNION SELECT apartment_number FROM charges UNION SELECT apartment_number FROM payments) x ORDER BY apartment_number;",db);
        var list=new List<int>(); await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) list.Add(r.GetInt32(0)); return list;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var db=new NpgsqlConnection(_connectionString);
        await db.OpenAsync();
        return db;
    }

    private static void Add(NpgsqlCommand cmd,string name,object value)
    {
        var p = cmd.Parameters.Add(name, value switch
        {
            DateOnly => NpgsqlDbType.Date,
            decimal => NpgsqlDbType.Numeric,
            int => NpgsqlDbType.Integer,
            long => NpgsqlDbType.Bigint,
            string => NpgsqlDbType.Text,
            DBNull => NpgsqlDbType.Text,
            _ => NpgsqlDbType.Text
        });
        p.Value = value;
    }

    private static void AddNullable(NpgsqlCommand cmd, string name, NpgsqlDbType type, long? value)
    {
        var parameter = cmd.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void ValidateApartmentAndPeriod(int apartment, DateOnly period)
    {
        if (apartment<=0) throw new InvalidOperationException("Номер квартиры должен быть положительным.");
        if (period.Day!=1) throw new InvalidOperationException("Период должен быть первым числом месяца.");
    }

    private static async Task<Saldo?> FindSaldoByPeriodAsync(NpgsqlConnection db,NpgsqlTransaction tx,int apartment,DateOnly period,long? excludedId)
    {
        await using var cmd=new NpgsqlCommand(@"SELECT id,opening_balance,closing_balance FROM saldo WHERE apartment_number=@a AND period=@p AND (@id IS NULL OR id<>@id) LIMIT 1;",db,tx);
        Add(cmd,"a",apartment);Add(cmd,"p",period);AddNullable(cmd,"id", NpgsqlDbType.Bigint, excludedId);
        await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync()) return null;
        return new Saldo{Id=r.GetInt64(0),ApartmentNumber=apartment,Period=period,OpeningBalance=r.GetDecimal(1),ClosingBalance=r.GetDecimal(2)};
    }

    private static async Task<Saldo?> FindSaldoByIdAsync(NpgsqlConnection db,NpgsqlTransaction tx,long id)
    {
        await using var cmd=new NpgsqlCommand("SELECT id,apartment_number,period,opening_balance,closing_balance FROM saldo WHERE id=@id;",db,tx);Add(cmd,"id",id);
        await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync()) return null;
        return new Saldo{Id=r.GetInt64(0),ApartmentNumber=r.GetInt32(1),Period=r.GetFieldValue<DateOnly>(2),OpeningBalance=r.GetDecimal(3),ClosingBalance=r.GetDecimal(4)};
    }

    private static async Task<(decimal Closing,decimal Opening)?> GetBoundarySaldoAsync(NpgsqlConnection db,NpgsqlTransaction tx,int apartment,DateOnly period,bool previous,long? excludedId=null)
    {
        var order=previous?"DESC":"ASC"; var op=previous?"<":">";
        var sql=$"SELECT opening_balance,closing_balance FROM saldo WHERE apartment_number=@a AND period {op} @p AND (@id IS NULL OR id<>@id) ORDER BY period {order} LIMIT 1;";
        await using var cmd=new NpgsqlCommand(sql,db,tx);Add(cmd,"a",apartment);Add(cmd,"p",period);AddNullable(cmd,"id", NpgsqlDbType.Bigint, excludedId);
        await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync()) return null;
        return (r.GetDecimal(1),r.GetDecimal(0));
    }

    private static async Task<decimal> SumChargesAsync(NpgsqlConnection db,NpgsqlTransaction tx,int apartment,DateOnly period)
    { await using var cmd=new NpgsqlCommand("SELECT COALESCE(SUM(amount),0) FROM charges WHERE apartment_number=@a AND period=@p;",db,tx);Add(cmd,"a",apartment);Add(cmd,"p",period);return Convert.ToDecimal(await cmd.ExecuteScalarAsync()); }
    private static async Task<decimal> SumPaymentsAsync(NpgsqlConnection db,NpgsqlTransaction tx,int apartment,DateOnly period)
    { await using var cmd=new NpgsqlCommand("SELECT COALESCE(SUM(amount),0) FROM payments WHERE apartment_number=@a AND period=@p;",db,tx);Add(cmd,"a",apartment);Add(cmd,"p",period);return Convert.ToDecimal(await cmd.ExecuteScalarAsync()); }

    private static async Task RebuildSaldoChainAsync(NpgsqlConnection db,NpgsqlTransaction tx,int apartment)
    {
        var rows=new List<(long Id,DateOnly Period,decimal Opening,decimal Closing)>();
        await using(var cmd=new NpgsqlCommand("SELECT id,period,opening_balance,closing_balance FROM saldo WHERE apartment_number=@a ORDER BY period;",db,tx))
        { Add(cmd,"a",apartment); await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) rows.Add((r.GetInt64(0),r.GetFieldValue<DateOnly>(1),r.GetDecimal(2),r.GetDecimal(3))); }
        decimal? previousClosing=null;
        foreach(var row in rows)
        {
            var opening=previousClosing??row.Opening;
            var charges=await SumChargesAsync(db,tx,apartment,row.Period); var payments=await SumPaymentsAsync(db,tx,apartment,row.Period); var closing=opening+charges-payments;
            if(row.Opening!=opening || row.Closing!=closing)
            {
                await using var cmd=new NpgsqlCommand("UPDATE saldo SET opening_balance=@o,closing_balance=@c WHERE id=@id;",db,tx);
                Add(cmd,"o",opening);Add(cmd,"c",closing);Add(cmd,"id",row.Id);await cmd.ExecuteNonQueryAsync();
            }
            previousClosing=closing;
        }
    }

    private static async Task EnsureChargeUniqueAsync(NpgsqlConnection db,NpgsqlTransaction tx,ChargeForm form)
    {
        await using var cmd=new NpgsqlCommand(@"SELECT 1 FROM charges WHERE apartment_number=@a AND period=@p AND amount=@amount AND (@id IS NULL OR id<>@id) LIMIT 1;",db,tx);
        Add(cmd,"a",form.ApartmentNumber);Add(cmd,"p",form.Period);Add(cmd,"amount",form.Amount);AddNullable(cmd,"id", NpgsqlDbType.Bigint, form.Id);
        if(await cmd.ExecuteScalarAsync() is not null) throw new InvalidOperationException("Начисление с такими же данными уже есть в базе.");
    }

    private static async Task EnsurePaymentUniqueAsync(NpgsqlConnection db,NpgsqlTransaction tx,PaymentForm form)
    {
        await using var cmd=new NpgsqlCommand(@"SELECT 1 FROM payments WHERE apartment_number=@a AND period=@p AND payment_date=@d AND amount=@amount AND (@id IS NULL OR id<>@id) LIMIT 1;",db,tx);
        Add(cmd,"a",form.ApartmentNumber);Add(cmd,"p",form.Period);Add(cmd,"d",form.PaymentDate);Add(cmd,"amount",form.Amount);AddNullable(cmd,"id", NpgsqlDbType.Bigint, form.Id);
        if(await cmd.ExecuteScalarAsync() is not null) throw new InvalidOperationException("Платеж с такими же данными уже есть в базе.");
        if(!string.IsNullOrWhiteSpace(form.PaymentReference))
        {
            await using var refCmd=new NpgsqlCommand(@"SELECT 1 FROM payments WHERE payment_reference=@ref AND (@id IS NULL OR id<>@id) LIMIT 1;",db,tx);Add(refCmd,"ref",form.PaymentReference);AddNullable(refCmd,"id", NpgsqlDbType.Bigint, form.Id);
            if(await refCmd.ExecuteScalarAsync() is not null) throw new InvalidOperationException("Номер/ссылка платежа уже используется.");
        }
    }

    private static async Task<int> GetApartmentByIdAsync(NpgsqlConnection db,NpgsqlTransaction tx,string table,long id)
    {
        await using var cmd=new NpgsqlCommand($"SELECT apartment_number FROM {table} WHERE id=@id;",db,tx);Add(cmd,"id",id); var value=await cmd.ExecuteScalarAsync();
        if(value is null) throw new KeyNotFoundException("Запись не найдена."); return (int)value;
    }

    private static async Task ExecuteDeleteAsync(NpgsqlConnection db,NpgsqlTransaction tx,string table,long id)
    { await using var cmd=new NpgsqlCommand($"DELETE FROM {table} WHERE id=@id;",db,tx);Add(cmd,"id",id);await cmd.ExecuteNonQueryAsync(); }
}
