using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Sammha.Infrastructure.Legacy;

/// <summary>one row of the old database, columns by name (case-insensitive); a missing column reads as null</summary>
public sealed class Row(Dictionary<string, object?> v)
{
    private readonly Dictionary<string, object?> _v = new(v, StringComparer.OrdinalIgnoreCase);
    public object? this[string c] => _v.TryGetValue(c, out var x) && x is not DBNull ? x : null;
    public IEnumerable<string> Columns => _v.Keys;

    public int? I(string c) => this[c] switch { null => null, int i => i, long l => (int)l, short s => s, byte b => b, decimal d => (int)d, double d => (int)d, string s when int.TryParse(s, out var i) => i, _ => null };
    public int Id => I("ID") ?? I("Id") ?? 0;
    public decimal? D(string c) => this[c] switch { null => null, decimal d => d, double d => (decimal)d, float f => (decimal)f, int i => i, long l => l, string s when decimal.TryParse(s, CultureInfo.InvariantCulture, out var d) => d, _ => null };
    public bool B(string c) => this[c] switch { bool b => b, int i => i != 0, byte b => b != 0, string s => s is "1" or "true" or "True", _ => false };
    public string? S(string c) { var s = this[c]?.ToString()?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
    public DateTime? Dt(string c) => this[c] switch { DateTime d => d, DateTimeOffset o => o.DateTime, DateOnly d => d.ToDateTime(TimeOnly.MinValue), _ => null };
}

/// <summary>reads the old Sammha SQL Server database (read only)</summary>
public sealed class LegacySource(string connectionString) : IAsyncDisposable
{
    private SqlConnection? _conn;
    private HashSet<string>? _tables;

    private async Task<SqlConnection> ConnAsync(CancellationToken ct)
    {
        if (_conn != null) return _conn;
        _conn = new SqlConnection(connectionString);
        await _conn.OpenAsync(ct);
        return _conn;
    }

    public async Task<bool> HasTableAsync(string table, CancellationToken ct)
    {
        if (_tables == null)
        {
            _tables = new(StringComparer.OrdinalIgnoreCase);
            await using var cmd = (await ConnAsync(ct)).CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo')";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) _tables.Add(r.GetString(0));
        }
        return _tables.Contains(table);
    }

    public async Task<int> CountAsync(string table, CancellationToken ct)
    {
        if (!await HasTableAsync(table, ct)) return -1;
        await using var cmd = (await ConnAsync(ct)).CreateCommand();
        cmd.CommandText = $"SELECT COUNT_BIG(*) FROM [dbo].[{Safe(table)}]";
        cmd.CommandTimeout = 600;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async IAsyncEnumerable<Row> StreamAsync(string table, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!await HasTableAsync(table, ct)) yield break;
        await using var cmd = (await ConnAsync(ct)).CreateCommand();
        cmd.CommandText = $"SELECT * FROM [dbo].[{Safe(table)}]";
        cmd.CommandTimeout = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var names = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        while (await r.ReadAsync(ct))
        {
            var d = new Dictionary<string, object?>(names.Length);
            for (var i = 0; i < names.Length; i++) d[names[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
            yield return new Row(d);
        }
    }

    public async Task<List<Row>> ReadAsync(string table, CancellationToken ct)
    {
        var list = new List<Row>();
        await foreach (var r in StreamAsync(table, ct)) list.Add(r);
        return list;
    }

    private static string Safe(string t) => t.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? t : throw new ArgumentException("bad table name");

    public async ValueTask DisposeAsync() { if (_conn != null) await _conn.DisposeAsync(); }
}

/// <summary>Arabic keyword matching for the old lookup tables</summary>
public static class Ar
{
    public static string Fold(string? s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
            sb.Append(c switch { 'أ' or 'إ' or 'آ' => 'ا', 'ة' => 'ه', 'ى' => 'ي', 'ـ' => '\0', _ => c });
        return sb.ToString().Replace("\0", "").Trim();
    }

    public static bool Has(string? s, params string[] keys) { var f = Fold(s); return keys.Any(k => f.Contains(Fold(k))); }
}
