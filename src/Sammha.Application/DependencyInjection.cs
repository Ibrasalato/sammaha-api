using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sammha.Application.Common;
using Sammha.Application.Engine;
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
        s.AddScoped<Sessions>();
        s.AddScoped<Mapper>();
        s.AddScoped<Notifier>();
        s.AddScoped<Matching>();
        s.AddScoped<Money>();
        s.AddScoped<AuctionEngine>();
        s.AddScoped<Sweeper>();

        // CQRS: every command and query goes through MediatR
        s.AddMediatR(m =>
        {
            m.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            m.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });
        return s;
    }
}
