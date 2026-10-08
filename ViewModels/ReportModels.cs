namespace Lab3Accounting.ViewModels;

public sealed class TurnoverRow
{
    public int ApartmentNumber { get; set; }
    public decimal? YearOpening { get; set; }
    public decimal? OpeningBalance { get; set; }
    public int MonthNo { get; set; }
    public DateOnly MonthStart { get; set; }
    public decimal ChargesTotal { get; set; }
    public decimal PaymentsTotal { get; set; }
    public decimal? ClosingBalance { get; set; }
    public DateTimeOffset? ActionAt { get; set; }
}

public sealed class ApartmentRow
{
    public int MonthNo { get; set; }
    public DateOnly MonthStart { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal ChargesTotal { get; set; }
    public decimal PaymentsTotal { get; set; }
    public decimal? ClosingBalance { get; set; }
    public DateTimeOffset? ActionAt { get; set; }
}

public sealed class DebtorRow
{
    public int ApartmentNumber { get; set; }
    public decimal LastMonthCharge { get; set; }
    public decimal Balance { get; set; }
    public decimal? OneMonth { get; set; }
    public decimal? TwoMonths { get; set; }
    public decimal? ThreeMonths { get; set; }
    public decimal? OverThreeMonths { get; set; }
    public decimal DebtMonths { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset? ActionAt { get; set; }
}
