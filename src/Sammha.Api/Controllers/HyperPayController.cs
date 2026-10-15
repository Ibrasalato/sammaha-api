using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sammha.Application.Features.Payments;
using Sammha.Infrastructure.Services;

namespace Sammha.Api.Controllers;

/// <summary>
/// HyperPay COPYandPAY: the page that shows HyperPay's payment form, and the address HyperPay sends the owner back to.
/// The payment id in the address is the only key; nothing is credited until HyperPay itself confirms the result.
/// </summary>
[ApiController, Route("api/payments/hyperpay"), AllowAnonymous]
public class HyperPayController(ISender mediator, HyperPayGateway hyperPay, IConfiguration cfg) : ControllerBase
{
    private static readonly Dictionary<string, (string Ar, string En)> Purposes = new()
    {
        ["deposit"] = ("ضمان أو رسوم طلب", "Request guarantee or fee"), ["invoice"] = ("سداد فاتورة", "Invoice payment"),
        ["bid_deposit"] = ("ضمان دخول مزاد", "Auction entry guarantee"), ["listing"] = ("شراء اسم", "Name purchase"),
        ["transfer"] = ("رسوم نقل ملكية", "Ownership transfer fee"), ["violation"] = ("سداد مخالفة", "Violation fine"), ["exemption"] = ("غرامة استثناء المشاركة بدون اسم", "Exemption fine"), ["topup"] = ("شحن المحفظة", "Wallet top-up"),
    };

    [HttpGet("{id:guid}/form")]
    public async Task<IActionResult> Form(Guid id, CancellationToken ct)
    {
        var f = await mediator.Send(new GetHyperPayFormQuery(id), ct);
        if (f == null) return Content(Page("انتهت صلاحية صفحة الدفع", "<p>ارجع إلى المنصة وابدأ الدفع من جديد.</p>"), "text/html; charset=utf-8");
        var api = (cfg["Payments:PublicApiUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        var result = $"{api}/api/payments/hyperpay/return?pid={id}";
        var (ar, _) = Purposes.GetValueOrDefault(f.Purpose, ("دفع", "Payment"));
        var integrity = string.IsNullOrEmpty(f.Integrity) ? "" : $" integrity=\"{WebUtility.HtmlEncode(f.Integrity)}\" crossorigin=\"anonymous\"";
        var back = f.CallbackUrl is { } cb && Allowed(cb) ? $"<a class=\"back\" href=\"{WebUtility.HtmlEncode(cb)}\">رجوع إلى المنصة دون دفع</a>" : "";
        var body =
            $"<p class=\"amount\">{f.Amount:#,0.##} ريال سعودي</p><p class=\"what\">{WebUtility.HtmlEncode(ar)} — منصة سمّها</p>" +
            "<script>var wpwlOptions = { locale: \"ar\", style: \"card\", brandDetection: true, paymentTarget: \"_top\", applePay: { displayName: \"Sammha\", total: { label: \"SAMMHA\" } } };</script>" +
            $"<script src=\"{WebUtility.HtmlEncode(hyperPay.WidgetScript(f.CheckoutId))}\"{integrity}></script>" +
            $"<form action=\"{WebUtility.HtmlEncode(result)}\" class=\"paymentWidgets\" data-brands=\"{HyperPayGateway.BrandsFor(f.Method)}\"></form>" + back;
        return Content(Page("الدفع الإلكتروني", body), "text/html; charset=utf-8");
    }

    [HttpGet("return")]
    public async Task<IActionResult> Return([FromQuery] Guid pid, [FromQuery] string? id, CancellationToken ct)
    {
        var r = await mediator.Send(new CompleteHyperPayCommand(pid, id), ct);
        var state = r.Paid ? "paid" : r.Pending ? "pending" : "failed";
        if (r.CallbackUrl is { } cb && Allowed(cb)) return Redirect(cb + (cb.Contains('?') ? "&" : "?") + "payment=" + state);
        return Content(Page(r.Paid ? "تم الدفع بنجاح" : "لم يكتمل الدفع", $"<p>{WebUtility.HtmlEncode(r.Message ?? "يمكنك العودة إلى المنصة الآن.")}</p>"), "text/html; charset=utf-8");
    }

    /// <summary>only send people back to the platform's own sites</summary>
    private bool Allowed(string url) => CallbackUrls.Allowed(url, CallbackUrls.Origins(cfg));

    private static string Page(string title, string body) => $$"""
        <!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}} — سمّها</title>
        <style>body{margin:0;background:#F4F1EA;font-family:Tajawal,system-ui,sans-serif;color:#1B2A22}main{max-width:520px;margin:32px auto;padding:24px;background:#fff;border-radius:16px;border:1px solid #E5E0D5}
        h1{font-size:22px;margin:0 0 12px;color:#0B4A33}.amount{font-size:28px;font-weight:800;margin:4px 0}.what{color:#6B7280;margin:0 0 16px}.back{display:block;margin-top:16px;text-align:center;color:#0B4A33;font-weight:700}</style>
        </head><body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}</main></body></html>
        """;
}
