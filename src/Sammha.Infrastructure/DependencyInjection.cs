using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sammha.Application.Common;
using Sammha.Infrastructure.Data;
using Sammha.Infrastructure.Services;

namespace Sammha.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddScoped<PostCommit>();
        s.AddScoped<IPostCommit>(sp => sp.GetRequiredService<PostCommit>());
        s.AddDbContext<SammhaDbContext>(o => o.UseSqlServer(cfg.GetConnectionString("Sammha"), sql =>
        {
            sql.CommandTimeout(30);
            sql.MigrationsAssembly(typeof(SammhaDbContext).Assembly.FullName);
        }));
        s.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<SammhaDbContext>());
        s.AddSingleton<WebhookQueue>();
        s.AddSingleton<IWebhookPublisher>(sp => sp.GetRequiredService<WebhookQueue>());
        s.AddHostedService<WebhookSender>();
        s.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(10));
        // card payments: HyperPay once its token is configured (or Payments:Provider = hyperpay), otherwise Moyasar if it has a key
        s.AddHttpClient<MoyasarGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));
        s.AddHttpClient<HyperPayGateway>(c => c.Timeout = TimeSpan.FromSeconds(30));
        s.AddTransient<IPaymentGateway>(sp =>
        {
            var p = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentOptions>>().Value;
            var provider = (p.Provider ?? "").Trim().ToLowerInvariant();
            if (provider == "hyperpay" || (provider == "" && p.HyperPay.Configured)) return sp.GetRequiredService<HyperPayGateway>();
            if (provider == "moyasar" || !string.IsNullOrEmpty(p.MoyasarSecretKey)) return sp.GetRequiredService<MoyasarGateway>();
            return new NoPaymentGateway();
        });
        s.AddSingleton<ISmsSender, WebhookSmsSender>();
        s.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        // wallet refunds straight to the owner's IBAN; simulated with simulated card payments, otherwise manual until the HyperPay payout API is connected
        s.Configure<PayoutOptions>(cfg.GetSection("Payouts"));
        if (cfg.GetValue("Payments:Simulate", false)) s.AddSingleton<IPayoutGateway, SimulatedPayoutGateway>();
        else s.AddSingleton<IPayoutGateway, ManualPayoutGateway>();
        s.AddScoped<DbSeeder>();
        s.AddScoped<Legacy.LegacyImporter>();
        s.AddSingleton<IActivityWriter, Services.ActivityWriter>();
        return s;
    }
}
