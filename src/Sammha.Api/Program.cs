using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sammha.Api.Infrastructure;
using Sammha.Application;
using Sammha.Application.Common;
using Sammha.Domain;
using Sammha.Infrastructure;
using Sammha.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ───── layers ─────
builder.Services.AddApplication(cfg);
builder.Services.AddInfrastructure(cfg);

// ───── web ─────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddSingleton<IAuctionRealtime, SignalRAuctionRealtime>();
builder.Services.AddHostedService<SweeperHost>();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    o.MaximumReceiveMessageSize = 4 * 1024; // clients only send auction ids
}).AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SubjectUserIdProvider>();

var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters (set it in user-secrets or an environment variable).");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = System.Security.Claims.ClaimTypes.Role, NameClaimType = "sub",
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], RequireExpirationTime = true, RequireSignedTokens = true,
    };
    // SignalR sends the token as ?access_token= (browsers cannot set headers on WebSockets)
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var t = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(t) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = t;
            return Task.CompletedTask;
        },
        OnChallenge = async ctx =>
        {
            // consistent JSON for the client; it refreshes once on 401 then signs out
            ctx.HandleResponse();
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers["WWW-Authenticate"] = "Bearer";
            await ctx.Response.WriteAsJsonAsync(new { message = "انتهت الجلسة، سجّل الدخول من جديد" });
        },
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("staff", p => p.RequireRole(Roles.Committee, Roles.Admin))
    .AddPolicy("admin", p => p.RequireRole(Roles.Admin));

var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? ["https://ibrasalato.github.io", "http://localhost:5173", "http://localhost:4173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(new { message = "محاولات كثيرة، انتظر دقيقة ثم حاول" }, ct);
    string Ip(HttpContext h) => h.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.AddPolicy("auth", h => RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("RateLimit:AuthPerMinute", 15), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("refresh", h => RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("RateLimit:RefreshPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("check", h => RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("RateLimit:CheckPerMinute", 90), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecurityTransformer>());

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MaintenanceMiddleware>();

app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auctions");
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));
// Swagger: the OpenAPI document at /openapi/v1.json and its UI at /swagger (Swagger:Enabled, on by default)
if (cfg.GetValue("Swagger:Enabled", true))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Sammha API v1");
        o.DocumentTitle = "Sammha API";
        o.EnablePersistAuthorization();
        o.DisplayRequestDuration();
    });
}

if (cfg.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().RunAsync();
}

app.Run();

public partial class Program;
