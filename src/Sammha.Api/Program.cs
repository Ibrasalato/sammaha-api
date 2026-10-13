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
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters (set it in user-secrets or an environment variable).");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1), RoleClaimType = System.Security.Claims.ClaimTypes.Role, NameClaimType = "sub",
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
    o.AddPolicy("auth", h => RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("check", h => RateLimitPartition.GetFixedWindowLimiter(Ip(h), _ => new FixedWindowRateLimiterOptions { PermitLimit = 90, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auctions");
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));
if (app.Environment.IsDevelopment()) app.MapOpenApi();

if (cfg.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().RunAsync();
}

app.Run();

public partial class Program;
