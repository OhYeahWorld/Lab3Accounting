namespace Lab3Accounting.Models;

public sealed class Payment
{
    public long Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly PaymentDate { get; set; }
    public TimeOnly PaymentTime { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
