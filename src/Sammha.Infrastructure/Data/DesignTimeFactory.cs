using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sammha.Infrastructure.Data;

/// <summary>Lets `dotnet ef migrations add` run without the web host.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<SammhaDbContext>
{
    public SammhaDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("SAMMHA_DB") ?? "Server=localhost;Database=Sammha;Trusted_Connection=True;TrustServerCertificate=True";
        return new SammhaDbContext(new DbContextOptionsBuilder<SammhaDbContext>().UseSqlServer(cs).Options);
    }
}
