using Npgsql;

namespace Lab3Accounting.Data;

public sealed class DatabaseInitializer
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IConfiguration configuration, IWebHostEnvironment environment, ILogger<DatabaseInitializer> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        await WaitForPostgresAsync(connectionString, cancellationToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var schema = await File.ReadAllTextAsync(Path.Combine(_environment.ContentRootPath, "db", "schema.sql"), cancellationToken);
        await using (var command = new NpgsqlCommand(schema, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var seed = await File.ReadAllTextAsync(Path.Combine(_environment.ContentRootPath, "db", "seed.sql"), cancellationToken);
        await using (var command = new NpgsqlCommand(seed, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var verify = new NpgsqlCommand(@"
            SELECT
                (SELECT COUNT(*) FROM saldo) AS saldo_count,
                (SELECT COUNT(*) FROM charges) AS charges_count,
                (SELECT COUNT(*) FROM payments) AS payments_count;", connection))
        await using (var reader = await verify.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Не удалось проверить состояние БД после инициализации.");

            var saldoCount = reader.GetInt64(0);
            var chargesCount = reader.GetInt64(1);
            var paymentsCount = reader.GetInt64(2);

            if (saldoCount == 0 || chargesCount == 0)
                throw new InvalidOperationException(
                    $"Инициализация завершилась без ожидаемых данных: saldo={saldoCount}, charges={chargesCount}, payments={paymentsCount}.");

            _logger.LogInformation(
                "PostgreSQL готов. Записей: saldo={SaldoCount}, charges={ChargesCount}, payments={PaymentsCount}.",
                saldoCount, chargesCount, paymentsCount);
        }
    }

    private static async Task WaitForPostgresAsync(string connectionString, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
            }
        }

        throw new InvalidOperationException("PostgreSQL is not available. Start docker compose first.", last);
    }
}
