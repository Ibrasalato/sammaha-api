using System.Text.Json;
using Sammha.Infrastructure.Legacy;

namespace Sammha.Api.Infrastructure;

/// <summary>
/// dotnet Sammha.Api.dll legacy inspect --source "&lt;old db connection&gt;" [--map-out legacy-map.json]
/// dotnet Sammha.Api.dll legacy import  --source "&lt;old db connection&gt;" [--map legacy-map.json] [--commit]
/// The target is this API's own ConnectionStrings:Sammha. Without --commit the import is a dry run (rolled back).
/// </summary>
public static class LegacyCli
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        string? Opt(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var verb = args.Length > 1 ? args[1] : "";
        var source = Opt("--source") ?? Environment.GetEnvironmentVariable("SAMMHA_LEGACY_DB");
        if (verb is not ("inspect" or "import") || string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("usage: legacy inspect|import --source \"<old database connection string>\" [--map legacy-map.json] [--map-out legacy-map.json] [--commit]");
            return 2;
        }
        await using var scope = services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<LegacyImporter>();
        await using var src = new LegacySource(source);
        var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        if (verb == "inspect")
        {
            var map = await importer.InspectAsync(src, Console.Out, CancellationToken.None);
            if (Opt("--map-out") is { } path)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(map, json));
                Console.WriteLine($"lookup map written to {path} — edit it and pass --map to import");
            }
            return 0;
        }
        var commit = args.Contains("--commit");
        var mapped = LegacyImporter.Merge(await LegacyImporter.AutoMapAsync(src, CancellationToken.None), Opt("--map"));
        Console.WriteLine(commit ? "IMPORT (will be saved)…" : "DRY RUN (nothing is saved; add --commit to save)…");
        var started = DateTime.UtcNow;
        var report = await importer.ImportAsync(src, mapped, commit, CancellationToken.None);
        report.Print(Console.Out);
        Console.WriteLine($"{(commit ? "saved" : "rolled back")} in {(DateTime.UtcNow - started).TotalSeconds:N0}s");
        return 0;
    }
}
