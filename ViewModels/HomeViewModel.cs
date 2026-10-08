namespace Lab3Accounting.ViewModels;

public sealed class HomeViewModel
{
    public int SaldoCount { get; set; }
    public int ChargesCount { get; set; }
    public int PaymentsCount { get; set; }
    public int ApartmentCount { get; set; }
    public decimal TotalDebtorBalance { get; set; }
    public DateTimeOffset? LastActionAt { get; set; }
}
