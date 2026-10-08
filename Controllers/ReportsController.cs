using Lab3Accounting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lab3Accounting.Controllers;

[Route("reports")]
public sealed class ReportsController : Controller
{
    private readonly LedgerService _ledger;
    public ReportsController(LedgerService ledger) => _ledger = ledger;

    [HttpGet("turnover")]
    public async Task<IActionResult> Turnover(int year=2017) => View(await _ledger.GetTurnoverAsync(year));

    [HttpGet("apartment")]
    public async Task<IActionResult> Apartment(int apartment=1,int year=2017)
    {
        ViewBag.Apartment = apartment; ViewBag.Year = year; ViewBag.Apartments = await _ledger.GetApartmentNumbersAsync();
        return View(await _ledger.GetApartmentReportAsync(apartment,year));
    }

    [HttpGet("debtors")]
    public async Task<IActionResult> Debtors(DateOnly? asOf)
    {
        var date=asOf ?? new DateOnly(2017,10,1);
        ViewBag.AsOf=date;
        return View(await _ledger.GetDebtorsAsync(date));
    }
}
