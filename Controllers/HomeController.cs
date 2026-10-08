using Lab3Accounting.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lab3Accounting.Controllers;

public sealed class HomeController : Controller
{
    private readonly LedgerService _ledger;
    public HomeController(LedgerService ledger) => _ledger = ledger;

    public async Task<IActionResult> Index() => View(await _ledger.GetHomeAsync());

    [Route("/Home/Error")]
    public IActionResult Error() => View();
}
