using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sammha.Api.Infrastructure;

/// <summary>Describes the API and the JWT bearer scheme, so Swagger UI can send the access token.</summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Sammha API",
            Version = "v1",
            Description = "منصة سمّها لمزادات أسماء الإبل. سجّل الدخول عبر /api/auth/signin ثم /api/auth/signin/complete، وانسخ قيمة token إلى زر Authorize.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token (15 minutes). Renew it with POST /api/auth/refresh.",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
