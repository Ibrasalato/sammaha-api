using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sammha.Domain;

namespace Sammha.Api.Infrastructure;

/// <summary>Business errors become {"message": "..."} with their status; anything else is logged and hidden.</summary>
public class ErrorHandler(ILogger<ErrorHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception e, CancellationToken ct)
    {
        var (status, message) = e switch
        {
            DomainException d => (d.Status, d.Message),
            DbUpdateConcurrencyException => (409, "تغيّرت البيانات أثناء العملية، حاول مرة أخرى"),
            SqlException { Number: 50001 } => (409, "المنصة مشغولة بعملية أخرى على نفس البيانات، حاول بعد لحظات"),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "البيانات مسجلة مسبقًا"),
            BadHttpRequestException b => (b.StatusCode, "طلب غير صالح"),
            OperationCanceledException => (499, "أُلغي الطلب"),
            _ => (500, "حدث خطأ غير متوقع، حاول مرة أخرى"),
        };
        if (status >= 500) log.LogError(e, "Unhandled error on {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { message }, ct);
        return true;
    }
}
