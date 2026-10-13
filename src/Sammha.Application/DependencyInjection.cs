using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sammha.Application.Common;
using Sammha.Application.Engine;
using Sammha.Application.Features;
using Sammha.Domain;

namespace Sammha.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<PlatformOptions>(cfg.GetSection("Platform"));
        s.Configure<OtpOptions>(cfg.GetSection("Otp"));
        s.Configure<IntegrationOptions>(cfg.GetSection("Integrations"));
        s.Configure<PaymentOptions>(cfg.GetSection("Payments"));
        s.AddSingleton(TimeProvider.System);

        s.AddScoped<Access>();
        s.AddScoped<Mapper>();
        s.AddScoped<Notifier>();
        s.AddScoped<Matching>();
        s.AddScoped<Money>();
        s.AddScoped<AuctionEngine>();
        s.AddScoped<Sweeper>();

        s.AddScoped<AuthService>();
        s.AddScoped<CatalogService>();
        s.AddScoped<RequestService>();
        s.AddScoped<BiddingService>();
        s.AddScoped<WalletService>();
        s.AddScoped<ListingService>();
        s.AddScoped<TicketService>();
        s.AddScoped<AdminService>();
        s.AddScoped<PaymentService>();
        return s;
    }
}
