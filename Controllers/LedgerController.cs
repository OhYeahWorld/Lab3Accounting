using Lab3Accounting.Services;
using Lab3Accounting.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Lab3Accounting.Controllers;

[Route("ledger")]
public sealed class LedgerController : Controller
{
    private readonly LedgerService _ledger;
    public LedgerController(LedgerService ledger) => _ledger = ledger;

    [HttpGet("saldo")]
    public async Task<IActionResult> Saldo(int? apartment) => View(await _ledger.GetSaldoAsync(apartment));

    [HttpPost("saldo/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSaldo(SaldoForm form)
    {
        try { if (form.Id.HasValue) await _ledger.UpdateSaldoAsync(form); else await _ledger.SaveSaldoAsync(form); TempData["ok"]="Сальдо сохранено."; }
        catch(Exception ex){TempData["error"]=ex.Message;}
        return RedirectToAction(nameof(Saldo));
    }

    [HttpGet("charges")]
    public async Task<IActionResult> Charges(int? apartment) => View(await _ledger.GetChargesAsync(apartment));

    [HttpPost("charges/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCharge(ChargeForm form)
    {
        try { await _ledger.SaveChargeAsync(form); TempData["ok"]="Начисление сохранено."; }
        catch(Exception ex){TempData["error"]=ex.Message;}
        return RedirectToAction(nameof(Charges));
    }

    [HttpGet("payments")]
    public async Task<IActionResult> Payments(int? apartment) => View(await _ledger.GetPaymentsAsync(apartment));

    [HttpPost("payments/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePayment(PaymentForm form)
    {
        try { await _ledger.SavePaymentAsync(form); TempData["ok"]="Платеж сохранен."; }
        catch(Exception ex){TempData["error"]=ex.Message;}
        return RedirectToAction(nameof(Payments));
    }

    [HttpPost("delete/{table}/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string table,long id)
    {
        try { await _ledger.DeleteAsync(table,id); TempData["ok"]="Запись удалена."; }
        catch(Exception ex){TempData["error"]=ex.Message;}
        return table switch { "saldo" => RedirectToAction(nameof(Saldo)), "charges" => RedirectToAction(nameof(Charges)), _ => RedirectToAction(nameof(Payments)) };
    }
}
