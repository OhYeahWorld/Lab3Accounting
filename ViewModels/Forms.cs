namespace Lab3Accounting.ViewModels;

public sealed class SaldoForm
{
    public long? Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public decimal OpeningBalance { get; set; }
}

public sealed class ChargeForm
{
    public long? Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "Начисление по квартире";
}

public sealed class PaymentForm
{
    public long? Id { get; set; }
    public int ApartmentNumber { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentReference { get; set; }
}
