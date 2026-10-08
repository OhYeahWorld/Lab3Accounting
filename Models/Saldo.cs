namespace Lab3Accounting.Models;

public sealed class Saldo
{
    public long Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
