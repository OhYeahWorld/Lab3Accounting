namespace Lab3Accounting.Models;

public sealed class Charge
{
    public long Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "Начисление по квартире";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
