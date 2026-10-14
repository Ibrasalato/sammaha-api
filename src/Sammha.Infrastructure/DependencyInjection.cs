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
        s.AddHttpClient<IPaymentGateway, MoyasarGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));
        s.AddSingleton<ISmsSender, WebhookSmsSender>();
        s.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        s.AddScoped<DbSeeder>();
        s.AddSingleton<IActivityWriter, Services.ActivityWriter>();
        return s;
    }
}
